# Pool-Play Standings Calculation

This document describes how the Application layer calculates pool-play standings and applies the
6-step tiebreaker chain. Normative tournament rules live in [`BusinessRules.md`](BusinessRules.md);
this file is the implementation map for developers and coding agents working on standings code.

---

## 1. Entry Points & Layering

| Component | Role |
|---|---|
| `PoolPlayStandingsCalculator` | Pure, synchronous in-memory calculator (no I/O). Prefer this in unit tests. |
| `IPoolPlayStandingsService` / `PoolPlayStandingsService` | Async facade: loads division data from `ITournamentDataContext`, then calls the calculator. |
| `PoolPlayStandingsSnapshotMapper` | Maps calculator DTOs → `DivisionStandingsSnapshotDocument` for persistence on the per-division JSON file. |
| `DivisionSizeRules` | Division-size matrix helpers (teams/pool, expected game counts, combined/winner-runner-up flags). |

All of the above live under `src/Region42.PantherShootout.Application/`. Register services with
`services.AddApplication()`.

Typical call flow:

1. Score entry / refresh orchestration loads the tournament year + division into `ITournamentDataContext`.
2. `PoolPlayStandingsService.CalculateForDivisionAsync(divisionId)` (or `BuildPoolPlaySnapshotAsync`) runs.
3. `PoolPlayStandingsCalculator.CalculateDivision(...)` produces per-pool results plus optional
   10-team combined / 12-team pool-winner & runner-up rankings.
4. Snapshot mapper writes the result into `DivisionDataDocument.Standings` (PSO section left default
   until the PSO calculator runs).

---

## 2. Per-Game Points (AYSO 10-Point System)

A game counts toward standings only when **both** `HomeGoals` and `AwayGoals` are non-null.

| Outcome | Points |
|---|---|
| Win | 6 |
| Draw | 3 |
| Loss | 0 |
| +1 per goal scored | capped at **3** per game |
| +1 shutout bonus | when goals against == 0, **not** awarded on forfeits |
| Forfeit winner (entered 1–0, `IsForfeit = true`) | **7** (6 win + 1 goal, no shutout) |
| Forfeit loser | 0 |
| Yellow card | −1 each (summed across the team's games) |
| Red card | −2 each |

Standings `Points` = sum of per-game contributions across all of the team's completed pool games.

### Display stats vs. tiebreaker helpers

Both sets of values are calculated and stored:

| Field | Meaning |
|---|---|
| `GoalsFor` / `GoalsAgainst` / `GoalDifferential` | Uncapped totals for the standings table (`GF` / `GA` / `GD`). |
| `CappedGoalsAgainst` | Sum of per-game `min(GA, 3)` over **non-forfeit** games only. |
| `CappedGoalDifferential` | Sum of `min(GF, 3) − min(GA, 3)` over **non-forfeit** games only. |
| `HasForfeit` | True if any of the team's completed games was a forfeit. |

Forfeit games still update uncapped `GF`/`GA`/`GD` and W/L, but are excluded from the capped
tiebreaker totals.

---

## 3. Six-Step Tiebreaker Chain

Primary sort key is **Points** (descending). Teams tied on points are broken in this order:

1. **Head-to-head** (descending) — only for a **2-way** tie; skipped for 3+ teams tied on points.
   The single completed game between the two teams awards H2H = 1 to the winner (a draw leaves
   both at 0 and falls through).
2. **Games won** (descending) — **nullified** (treated equal for everyone) if any forfeit exists
   in the ranking **scope** (see §4).
3. **Misconduct** (ascending) — fewer red cards first, then fewer yellow cards.
4. **Capped goals against** (ascending) — fewer non-forfeit capped GA is better.
5. **Capped goal differential** (descending) — **nullified** if any forfeit exists in scope.
6. **KFTM override** (descending) — manual `TiebreakerOverride` records with
   `Scope = PoolPlayStandings`. Within a still-tied group, a team’s KFTM score is the count of
   override wins against other teams in that same group. PSO-scoped overrides are ignored.

Stable fallbacks after KFTM: `TeamNumber`, then `TeamName` (ordinal-ignore-case).

### Completeness & KFTM alerts

- `IsFinal` is true when the number of completed games in the ranking scope meets the expected
  round-robin count (`n * (n − 1) / 2` per pool, from `DivisionSizeRules`).
- `RequiresKftmTiebreaker` / `TiedTeamIdsForKftm` are set only when the scope is final and two or
  more teams still share an identical **statistical signature** (points, H2H, effective wins,
  cards, capped GA/GD) that KFTM has not yet differentiated.

---

## 4. Division-Size Special Cases

| Size | Per-pool tables | Extra rankings | Forfeit “scope” for wins/GD nullification |
|---|---|---|---|
| 4 / 5 / 6 / 8 | Yes | — | That pool only |
| 10 (2×5) | Yes | `CombinedDivisionStandings` across both pools | **Whole division** (a forfeit in either pool nullifies wins/GD for all pool and combined sorts) |
| 12 (3×4) | Yes | `PoolWinnersStandings` (#1 from each pool) and `RunnersUpStandings` (#2 from each pool), re-ranked with the same tiebreaker chain (H2H usually N/A because cross-pool teams never played) | Whole division for the sub-rankings; per-pool for each pool table |

`DivisionSizeRules` is the single source for pool counts, teams per pool, expected games, and
these feature flags (`UsesCombinedDivisionStandings`, `UsesPoolWinnerRunnerUpRanking`,
`UsesDivisionWideForfeitScope`).

Champion hint on the snapshot (`ChampionTeamId`) is set only when all pools are final, and only
for single-pool divisions or the 10-team combined #1. Multi-pool bracket champions are resolved
later by championship bracket progression.

---

## 5. Snapshot Shape

`DivisionStandingsSnapshotDocument` (on `DivisionDataDocument.Standings`) stores:

- `CalculatedAt`, optional `ChampionTeamId`
- `Pools[]` — one `PoolStandingsSnapshotDocument` per pool (`IsFinal`, forfeit/KFTM flags, team rows)
- `CombinedDivisionStandings` — 10-team only
- `PoolWinnersStandings` / `RunnersUpStandings` — 12-team only
- `PsoStandings` — filled by the PSO calculator (separate competition)

Team rows mirror calculator fields (`Rank`, `Points`, uncapped and capped goal stats, cards,
`HasForfeit`). Display names/designations are **not** stored on the snapshot; resolve them from
`tournament.json` team records by `TeamId`.

---

## 6. Key Source Files

| Path | Purpose |
|---|---|
| `Application/Services/PoolPlayStandingsCalculator.cs` | Scoring + tiebreaker algorithm |
| `Application/Services/PoolPlayStandingsService.cs` | Context load + calculate / snapshot build |
| `Application/DTOs/PoolPlayStandingsDtos.cs` | Calculator result DTOs |
| `Application/Helpers/DivisionSizeRules.cs` | Size matrix |
| `Application/Helpers/PoolPlayStandingsSnapshotMapper.cs` | DTO → document mapping |
| `Domain/Documents/DivisionDataDocument.cs` | Snapshot document POCOs |
| `Domain/Entities/TiebreakerOverride.cs` + `Enums/TiebreakerScope.cs` | KFTM override model |
| `test/.../Application.Tests/Services/PoolPlayStandingsCalculatorTests.cs` | Rule coverage (points, forfeits, H2H, 3-way, KFTM, 10/12-team) |
