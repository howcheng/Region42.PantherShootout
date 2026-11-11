# Championship Bracket Generation & Progression

This document maps how the Application layer builds championship brackets, seeds them from
pool-play standings, progresses dependent slots, and syncs 6-team PSO Round 4 pairings.
Normative rules live in [`BusinessRules.md`](BusinessRules.md); pool-play seeding inputs are
described in [`pool-play-standings.md`](pool-play-standings.md).

---

## 1. Entry Points & Layering

| Component | Role |
|---|---|
| `ChampionshipBracketBuilder` | Pure, synchronous shell definitions, seeding, SF progression, champion resolution, PSO Round 4 assignment helpers. |
| `IChampionshipBracketService` / `ChampionshipBracketService` | Async facade: loads pool standings via `IPoolPlayStandingsService`, mutates tracked `ChampionshipGame` / `ShootoutRound` entities, updates snapshot `ChampionTeamId`. |
| `DivisionSizeRules.HasChampionshipBracket` | False for 5- and 10-team divisions (pool/combined #1 is the champion). |

Register with `services.AddApplication()`. `SeedAndProgressAsync` mutates in-memory entities but
does **not** call `SaveChanges` — persistence is left to score-entry / refresh orchestration.

---

## 2. Bracket Topologies by Division Size

| Size | Bracket games | How teams are chosen |
|---|---|---|
| 4 (1×4) | `Consolation` (3rd vs 4th), `Championship` (1st vs 2nd) | Single-pool ranks |
| 5 (1×5) | *None* | Pool #1 is champion when pool play is final |
| 6 (2×3) | `Consolation` (A3 vs B3), `Semifinal1` (A1 vs B2), `Semifinal2` (B1 vs A2), `ThirdPlace` (SF losers), `Championship` (SF winners) | Opening rounds from pool ranks; Final/3rd from SF results |
| 8 (2×4) | `ThirdPlace` (A2 vs B2), `Championship` (A1 vs B1) | Pool ranks (no semifinals) |
| 10 (2×5) | *None* | Combined division #1 is champion when final |
| 12 (3×4) | `ThirdPlace` (3rd pool winner vs 1st runner-up), `Championship` (1st vs 2nd pool winner) | Cross-pool winner/runner-up sub-rankings; 2nd/3rd runners-up are eliminated |

`GetSlotDefinitions(size)` / `CreateBracketShells(divisionId, size)` produce the empty
`ChampionshipGame` shells (source descriptions only, no team IDs). Shells are typically created
during CSV import; seeding fills them later.

---

## 3. Seeding & Progression Flow

`SeedAndProgress(division, poolStandings, championshipGames)`:

1. **Pool-play gate** — Opening rounds are seeded only when `AllPoolsFinal` is true (and, for
   10-team, combined standings are final). Incomplete pools leave bracket team IDs untouched.
2. **Opening-round seed** — Resolves `HomeTeamId` / `AwayTeamId` from pool ranks (or 12-team
   `PoolWinnersStandings` / `RunnersUpStandings`).
3. **6-team progression** — When both semifinals have a determined winner (not a draw), copies
   SF losers into `ThirdPlace` and SF winners into `Championship`. Re-runs safely after score
   corrections.
4. **Champion** —
   - Bracket divisions: winner of the `Championship` game once both scores are entered.
   - 5-team: pool #1 when final.
   - 10-team: combined division #1 when final.
5. **PSO Round 4 assignments** (6-team only) — Emits pairings for `Consolation`, `Semifinal1`,
   and `Semifinal2`. `ApplyPsoRound4Assignments` copies those team IDs onto existing Round 4
   `ShootoutRound` rows linked by `ChampionshipGameId`.

A championship game has a determined winner only when both team IDs are set, both goals are
entered, and home/away goals are unequal (`GameWinner.Home` / `Away`). Draws do not progress
dependent slots.

---

## 4. Source Descriptions

Human-readable placeholders stored on each shell until teams are known, for example:

- `"1st place"` / `"Pool A 1st place"` / `"1st-ranked pool winner"`
- `"Loser Semifinal 1"` / `"Winner Semifinal 2"` (progression slots)

UI should prefer resolved team names when `HomeTeamId` / `AwayTeamId` are set, falling back to
these descriptions for unseeded brackets.

---

## 5. Key Source Files

| Path | Purpose |
|---|---|
| `Application/Services/ChampionshipBracketBuilder.cs` | Shells, seed, progress, PSO R4 sync |
| `Application/Services/ChampionshipBracketService.cs` | Context + standings orchestration |
| `Application/DTOs/ChampionshipBracketDtos.cs` | Slot definitions and bracket result DTOs |
| `Domain/Enums/BracketSlot.cs` | `Consolation`, `Semifinal1`, `Semifinal2`, `ThirdPlace`, `Championship` |
| `Domain/Entities/ChampionshipGame.cs` | Bracket game entity |
| `test/.../ChampionshipBracketBuilderTests.cs` | All six sizes + SF→Final coverage |
| `test/.../ChampionshipBracketServiceTests.cs` | PSO R4 sync + champion snapshot update |
