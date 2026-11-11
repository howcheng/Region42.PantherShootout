# Score Entry & Standings Refresh

This document maps how admin score / KFTM updates flow into recalculated standings snapshots.
Related calculators: [`pool-play-standings.md`](pool-play-standings.md),
[`championship-brackets.md`](championship-brackets.md), [`pso-standings.md`](pso-standings.md).

---

## 1. Entry Points

| Component | Role |
|---|---|
| `IScoreEntryService` / `ScoreEntryService` | Validates and applies pool-play, championship, PSO, and KFTM updates; then refreshes the division. |
| `IStandingsRefreshService` / `StandingsRefreshService` | Recalculates pool standings, seeds/progresses brackets (+ 6-team PSO Round 4), recalculates PSO, writes `DivisionStandingsSnapshotDocument`, optionally `SaveChangesAsync`. |

Both are registered in `AddApplication()`.

---

## 2. Score Entry Rules

### Pool-play (`SavePoolPlayScoreAsync`)
- Updates goals, `IsForfeit`, and per-team yellow/red cards on `PoolPlayGame`.
- Home/away goals must both be set or both cleared.
- Forfeits require a **1–0** (or **0–1**) score.
- Card counts cannot be negative.

### Championship (`SaveChampionshipScoreAsync`)
- Requires both `HomeTeamId` and `AwayTeamId` already seeded.
- Same goal-pair and forfeit rules as pool play (no cards on championship games).

### PSO (`SavePsoScoreAsync`)
- Requires both teams assigned on the `ShootoutRound`.
- Home/away PK goals both set or both cleared.

### KFTM (`UpsertKftmOverrideAsync` / `RemoveKftmOverrideAsync`)
- `Scope` is `PoolPlayStandings` or `PsoStandings` (independent).
- `WinningTeamId` must be one of the two teams.
- Team pair is normalized to `TeamAId < TeamBId` for upsert matching.
- Remove deletes the override entity then refreshes.

Every successful save path ends with `RefreshDivisionAsync(divisionId, saveChanges: true)`.

---

## 3. Refresh Pipeline

`RefreshDivisionAsync(divisionId, saveChanges = true)`:

1. `IPoolPlayStandingsService.CalculateForDivisionAsync` → pool / combined / winner–runner-up tables.
2. `IChampionshipBracketService.SeedAndProgressAsync` → mutate opening bracket team IDs, progress 6-team SF results, sync PSO Round 4 pairings in memory.
3. `IPsoStandingsService.CalculateForDivisionAsync` → division-wide PSO table.
4. Build `DivisionStandingsSnapshotDocument` from pool mapper + PSO mapper; set `ChampionTeamId` from the bracket result (includes 5/10 no-bracket champions; null until a championship final has a winner for bracket sizes).
5. `ITournamentDataContext.SetStandingsForDivision` (marks the division JSON dirty).
6. Optionally `SaveChangesAsync` to flush dirty documents.

Pass `saveChanges: false` when batching inside a transaction (e.g. future CSV import).

---

## 4. Key Source Files

| Path | Purpose |
|---|---|
| `Application/Services/ScoreEntryService.cs` | Validation + entity updates |
| `Application/Services/StandingsRefreshService.cs` | Orchestration + snapshot write |
| `Application/DTOs/ScoreEntryDtos.cs` | Update DTOs |
| `test/.../ScoreEntryServiceTests.cs` | Validation and upsert coverage |
| `test/.../StandingsRefreshServiceTests.cs` | Snapshot merge / saveChanges flag |
