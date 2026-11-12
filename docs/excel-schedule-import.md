# Excel Workbook Schedule Import & AYSO Region Mapping

Tournament divisions, pools, discrete team rosters, pool-play schedules, championship bracket times/fields, and Penalty Shootout (PSO) rounds are imported from the tournament director's master **Excel (`.xlsx`) schedule workbook** via a two-step **Validate & Preview -> Confirm Import** workflow.

---

## 1. Entry Points & Layering

| Layer | Component | Responsibility |
| :--- | :--- | :--- |
| `Domain` | `AysoRegion` / `RegionsDocument` | Year-independent AYSO region number-to-name catalog (`panther-shootout/regions.json`). |
| `Domain` | `Team` / `TeamDocument` | Discrete team fields (`Code`, `TeamNumber`, `RegionNumber`, `RegionName`, `CoachName`, computed `Name`). |
| `Domain` | `IScheduleWorkbookReader` / `ParsedWorkbookData` | Framework-free contract for raw worksheet extraction. |
| `Infrastructure` | `OpenXmlScheduleWorkbookReader` | OpenXML (`DocumentFormat.OpenXml`) `.xlsx` reader inspecting **only** `Pools` and `Schedule`. |
| `Application` | `IAysoRegionService` / `AysoRegionService` | Async CRUD over global AYSO region mappings (preventing deletion of in-use regions). |
| `Application` | `IScheduleImportService` / `ScheduleImportService` | All-at-once validation, year-agnostic bracket heuristics + overrides, lock acquisition, and atomic commit. |

---

## 2. Discrete Team Properties & Year-Independent AYSO Regions

### `panther-shootout/regions.json` (`RegionsDocument`)
- Permanent AYSO region numbers (`42` -> `"Newbury Park"`, `121` -> `"Simi Valley"`, `638` -> `"Quartz Hill"`, etc.) never change across tournament years and are stored once at `{slug}/regions.json`.
- When `regions.json` does not yet exist on disk/GCS, `TournamentDataContext` hydrates `DefaultAysoRegions.Entries` in memory without marking the file dirty until an admin saves a change via `IAysoRegionService`.
- Renaming a region in `IAysoRegionService` immediately re-wires `Team.RegionName` and `Team.Name` (`"{RegionName}: {CoachName}"`) across all loaded tournament years without rewriting historical `tournament.json` files.

### Discrete `Team` Fields
Teams on the `Pools` sheet use a discrete code column (e.g. `A1`, `C4`) paired with `<DIV>-<REGION#>-<COACH>` (e.g. `10UG-121-Wright`):
- `Pool.Name` + `TeamNumber` -> `Team.Code` / `Team.GetDesignation()` (e.g. `"A1"`, `"C4"`). Note that pool letters are unique across the entire tournament (`A/B`, `C/D`, `E/F`, `G`, `I/J`, `K/L`).
- `RegionNumber` (`int`, e.g. `121`) -> resolved to `RegionName` (`"Simi Valley"`) via `AysoRegion`.
- `CoachName` (`string`, e.g. `"Wright"`).
- `Name` -> dynamically formatted as `"{RegionName}: {CoachName}"` (`"Simi Valley: Wright"`).

---

## 3. Workbook Structure & Year-to-Year Variation Handling

### Strictly `Pools` & `Schedule` Worksheets
- `OpenXmlScheduleWorkbookReader` reads **only** the worksheets named `Pools` and `Schedule` (case-insensitive, preferring visible if duplicate names exist).
- Any other worksheets in the `.xlsx` file (hidden scratch tabs from prior years, notes tabs, etc.) are completely ignored as junk data.

### `Pools` Worksheet
- Contains division blocks (`10U Girls`, `10U Boys`, `12U Girls`, `12U Boys`, `14U Girls`, `14U Boys`), each with `# | Pool 1 | # | Pool 2` (and `Pool 3` for 12-team divisions). Empty pool columns (such as an unused `Pool 2` header on a 4-team division) are ignored automatically.
- Validates total division team count against `DivisionSizeRules` (`4, 5, 6, 8, 10, 12`), pool count, teams per pool, 1-based contiguous numbering within each pool, division prefix consistency, and `AysoRegion` existence.

### `Schedule` Worksheet & Year-Agnostic Resolution
- **Day Grids & Inherited Field Headers**: Date rows store an Excel date in Column `A` and field headers (`Field 1`..`Field 7`) across Columns `B..H`. Subsequent day headers (e.g. Saturday) may omit unchanged field labels and automatically inherit them from Day 1. Time rows store start times in Column `A`.
- **Pool-Play Matchups**: Matched via `<HomeCode> (vs|v)[.] <AwayCode>` (e.g. `C1 vs C2` or `C1 v C2`). Chronological 1-based `Round` numbers are assigned per pool by grouping disjoint-team games up to `DivisionSizeRules.GetGamesPerPoolRound(size)`.
- **Championship & Consolation Placeholders**: Because neither background colors nor bracket wording are guaranteed to stay identical from year to year (e.g. `"Champ"` / `"Consol"` in 2025, `"Championship 1st E vs 1st F"` in 2024, `"Championship 1st Pool 1 v 1st Pool 2"` in 2022), non-pool-play cells are resolved through a prioritized chain:
  1. **Admin Cell Override (`ScheduleCellOverrideDto`)**: Keyed by cell coordinate (e.g. `E20`), allowing admins on the Preview screen to map any custom future wording/color to `(DivisionKey, BracketSlot)` or mark a non-game note cell `Ignore = true`.
  2. **Division Signal Chain**:
     - Explicit uppercase tournament pool letters in text (e.g. `"3rd A vs B"`, `"Championship 1st E vs 1st F"` -> resolves division even with zero fill color).
     - Dynamic per-workbook fill color map built from that file's `Color Key` legend, empirical fills on unambiguous pool-play cells, and `Pools` division header fills (within `<= 12` per-channel RGB tolerance).
  3. **BracketSlot Heuristics**:
     - 6-team opening consolation (`BracketSlot.Consolation`): `3rd`/`3rds`/`Seed 3` cross-pool matchups (`"3rd A vs B"`, `"3rds I vs J"`).
     - 6-team semifinals (`Semifinal1` / `Semifinal2`): `"Semifinal 1/2"`, `"SF1/2"`, or `"1st <Pool1> vs 2nd <Pool2>"` vs `"1st <Pool2> vs 2nd <Pool1>"` (supporting both pool letters and `Pool 1`/`Pool 2`).
     - Championship final (`BracketSlot.Championship`): `"Champ"`, `"Championship ..."`, `"Final"`, or `1st ... vs 1st ...`.
     - Consolation / 3rd Place: `"Consol"`, `"Consolation ..."`, `"3rd Place"`, `"3rd vs 4th"`, or `2nd ... vs 2nd ...` -> maps to `BracketSlot.Consolation` for 4-team divisions and `BracketSlot.ThirdPlace` for 6-, 8-, and 12-team divisions.
- Resolved `ChampionshipGame` shells store `ScheduledDateTime` and `Location` immediately so families and coaches can plan around bracket game times before pool play finishes.

---

## 4. PSO Shootout Shell Generation & Two-Step Commit

- **PSO Shells**:
  - `4`, `8`, `12` teams: 1 `ShootoutRound` per pool-play game (`Rounds 1–3`).
  - `5`, `10` teams: 1 `ShootoutRound` per pool-play game in `Rounds 1–4` (`Round 5` pool-play games do not have a shootout; `PsoStandingsCalculator` applies the 4th-round cap).
  - `6` teams: 1 `ShootoutRound` per pool-play game (`Rounds 1–3`) plus 3 `Round 4` `ShootoutRound` shells linked to the opening `ChampionshipGame` shells (`Consolation`, `Semifinal1`, `Semifinal2`).
- **Step 2 (`ImportWorkbookAsync`)**:
  1. Acquires `IStorageWriteLock` (`StorageWriteLockNames.ScheduleImport`).
  2. Re-validates the workbook (throwing `ScheduleImportValidationException` if invalid) and blocks overwriting divisions with recorded scores unless `allowOverwriteScores: true` is passed.
  3. Opens `ITournamentDataContext.BeginTransactionAsync()`, replaces divisions/pools/teams/games/PSO rounds, refreshes initial division standings snapshots (`saveChanges: false`), flushes dirty JSON documents in a single `SaveChangesAsync()`, and commits (rolling back in-memory state if persistence fails).
