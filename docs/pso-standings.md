# Penalty Shootout (PSO) Standings Calculation

This document maps the Application-layer PSO competition calculator. Normative rules are in
[`BusinessRules.md`](BusinessRules.md). PSO is a **separate, division-wide** competition from
pool-play / championship games; only PK scores on `ShootoutRound` records feed these standings.

---

## 1. Entry Points & Layering

| Component | Role |
|---|---|
| `PsoStandingsCalculator` | Pure, synchronous division-wide PSO ranking. |
| `IPsoStandingsService` / `PsoStandingsService` | Async facade over `ITournamentDataContext`. |
| `PsoStandingsSnapshotMapper` | Maps DTOs → `PsoStandingsSnapshotDocument`. |
| `DivisionSizeRules.GetShootoutRoundCount` / `UsesPsoFourthRoundCap` | 3 vs 4 scheduled rounds; 5-team 4th-round cap flag. |

Register with `services.AddApplication()`.

---

## 2. What Counts as a Result

A `ShootoutRound` counts for a team only when `IsComplete` is true (both team IDs and both
`HomePkGoals` / `AwayPkGoals` are set) **and** the team is home or away on that round.

| Division size | Scheduled PSO rounds | Notes |
|---|---|---|
| 4 / 8 / 12 | 3 | Even pools; each team plays every round. |
| 5 / 10 | 4 | Odd (5-team) pools with byes; **4th-round cap** applies. |
| 6 | 4 | Rounds 1–3 mirror pool play (with byes); Round 4 pairings come from opening championship games (`Consolation`, `Semifinal1`, `Semifinal2`) — see [`championship-brackets.md`](championship-brackets.md). |

Every team needs **3 counted matchups** (`DivisionSizeRules.RequiredPsoMatchupsPerTeam`) for
`IsFinal` / `WinnerTeamId`.

### 5-team 4th-round cap

If a team already has 3 completed results from rounds 1–3, round 4 is **excluded** from
`RoundScores` totals (avoids double-counting teams unaffected by a given bye). If the team has
fewer than 3 early results (because of byes), round 4 **is** counted.

---

## 3. Ranking & Tiebreakers

Primary sort: **total PK goals for** (descending), summed across counted rounds only.

Tiebreakers:

1. **PK goals against** (ascending), using the same counted-round set / 4th-round cap.
2. **KFTM** (`TiebreakerOverride` with `Scope = PsoStandings`) — independent of pool-play KFTM.
3. Stable fallback: team designation, then name.

When the division is final and two or more teams still share the same (goals for, goals against)
signature without a differentiating PSO KFTM, `RequiresKftmTiebreaker` is set and `WinnerTeamId`
stays null. Otherwise `WinnerTeamId` is the rank-1 team once `IsFinal` is true.

---

## 4. Snapshot Shape

`PsoStandingsSnapshotDocument` on `DivisionDataDocument.Standings.PsoStandings`:

- `IsFinal`, `WinnerTeamId`, `RequiresKftmTiebreaker`, `TiedTeamIdsForKftm`
- `Teams[]`: `Rank`, `TeamId`, `RoundScores` (nullable per scheduled round; counted only),
  `TotalPkGoalsFor`, `TotalPkGoalsAgainst`, `RoundsCompleted`

Team display names are resolved from `tournament.json` by `TeamId`, not stored on the snapshot.

---

## 5. Key Source Files

| Path | Purpose |
|---|---|
| `Application/Services/PsoStandingsCalculator.cs` | Scoring, 4th-round cap, tiebreakers |
| `Application/Services/PsoStandingsService.cs` | Context load + calculate / snapshot |
| `Application/DTOs/PsoStandingsDtos.cs` | Result DTOs |
| `Application/Helpers/PsoStandingsSnapshotMapper.cs` | DTO → document mapping |
| `Domain/Entities/ShootoutRound.cs` | PSO round entity (`IsComplete`) |
| `test/.../PsoStandingsCalculatorTests.cs` | 3-round, 5-team cap, 6-team R4, KFTM coverage |
