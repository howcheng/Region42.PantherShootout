# JSON File Storage Strategy

`Region42.PantherShootout` persists all tournament data as UTF-8 JSON documents in **Google Cloud Storage (GCS)** in production and on the **local filesystem** during development. There is no SQL database or Entity Framework Core layer.

---

## 1. Bucket Layout & Coexistence with `Region42.ScoresStandings`

Production shares the `gs://region42-storage` bucket with the sibling regular-season application (`Region42.ScoresStandings`).

Because `ScoresStandings` reads the root `index.json` without filtering by competition slug and scans for `*/season.json` objects when listing seasons, `PantherShootout` isolates its catalog, locks, and tournament metadata under its own prefix and filenames so neither application can collide with or ingest the other's files:

```text
gs://region42-storage/
  panther-shootout/
    index.json
    locks/
      csv-schedule-import.json
  {year}/
    panther-shootout/
      tournament.json
      10u-boys.json
      10u-girls.json
      12u-boys.json
      12u-girls.json
      14u-boys.json
      14u-girls.json
```

All object paths and division keys are generated centrally by `DivisionKeyHelper` (`src/Region42.PantherShootout.Domain/Helpers/DivisionKeyHelper.cs`).

---

## 2. Document Schemas & Responsibilities

Document POCOs live in `src/Region42.PantherShootout.Domain/Documents/` and are mapped to/from domain entities via `DocumentEntityMapper`.

### `panther-shootout/index.json` (`TournamentIndexDocument`)
- Top-level catalog of all Panther Shootout tournament years in the bucket.
- Stores `schemaVersion` and `tournaments[]` (`tournamentYearId`, `year`, `slug`, `name`, `isActive`, `tournamentPath`).

### `{year}/panther-shootout/tournament.json` (`TournamentDocument`)
- Authoritative metadata and roster hierarchy for a single tournament year.
- Contains:
  - `schemaVersion`, `version`, `modifiedAt`, `modifiedBy`
  - `tournament`: `TournamentYear` metadata (`id`, `year`, `name`, `isActive`, `customMessage`)
  - `divisions[]`: division config (`id`, `key`, `name`, `ageGroup`, `gender`, `size`, `customMessage`) -> `pools[]` (`id`, `name`) -> `teams[]` (`id`, `teamNumber`, `name`)
- **Single source of truth for team names**: Just like `season.json` in `ScoresStandings`, team names live only in `tournament.json`. Downstream game, bracket, shootout, override, and standings records reference teams strictly by integer `teamId` and resolve display names/designations (`A1`, etc.) in memory.

### `{year}/panther-shootout/{division-key}.json` (`DivisionDataDocument`)
- Partitioned per division (`10u-boys.json`, `12u-girls.json`, etc.) so scorekeepers entering results in different divisions never contend on the same file.
- Contains:
  - `schemaVersion`, `version`, `divisionKey`, `divisionId`, `modifiedAt`, `modifiedBy`
  - `poolPlayGames[]`: round, scheduled time/location, `homeTeamId`/`awayTeamId`, goals, `isForfeit`, per-team yellow/red cards, and audit metadata.
  - `championshipGames[]`: `BracketSlot`, scheduled time/location, `homeSourceDescription`/`awaySourceDescription`, resolved `homeTeamId`/`awayTeamId`, goals, and `isForfeit`.
  - `shootoutRounds[]`: PSO round number, `poolPlayGameId` or `championshipGameId` (used for 6-team Round 4), `homeTeamId`/`awayTeamId`, and `homePkGoals`/`awayPkGoals`.
  - `tiebreakerOverrides[]`: manual KFTM winners scoped independently to `PoolPlayStandings` or `PsoStandings`.
  - `standings`: pre-calculated `DivisionStandingsSnapshotDocument` (per-pool standings, optional combined division standings for 10-team divisions, division champion ID, and division-wide PSO standings).

### `panther-shootout/locks/{lockName}.json` (`StorageWriteLockDocument`)
- Ephemeral lock file containing `holder`, `acquiredAtUtc`, and `expiresAtUtc` used by `IStorageWriteLock`.

---

## 3. Data Access Architecture

### `ITournamentDataStore` (Low-Level Object I/O)
- **`GcsTournamentDataStore`**: Reads/writes JSON objects in GCS using `Google.Cloud.Storage.V1.StorageClient` and wraps payloads in `StorageDocument<T>` carrying the GCS object `Generation`.
- **`LocalFileTournamentDataStore`**: Mirrors the exact bucket directory structure on local disk under `Storage:LocalRoot`. Writes atomically via a `.tmp` file rename and tracks simulated object generations in a `<filename>.generation` sidecar file so concurrency semantics behave identically in local development and tests.
- Serialization uses camelCase property names, enum strings, indented formatting, and UTF-8 **without** BOM.

### `ITournamentDataContext` & `IRepository<T>` (Scoped Unit of Work & CRUD)
- **`TournamentDataContext`** is registered as **scoped** per HTTP request:
  - **Lazy Hydration & Wiring**: Loads `panther-shootout/index.json`, active/requested `tournament.json` files, and `{division-key}.json` files into in-memory entity collections and wires object navigation properties (`Division` <-> `Pool` <-> `Team`, `PoolPlayGame`/`ChampionshipGame` <-> `ShootoutRound`).
  - **ID Allocation**: Computes `_nextId = max(existing entity IDs) + 1` across loaded entities and assigns sequential integer IDs when `entity.Id == 0`.
  - **Audit Stamping**: Uses `ICurrentUserService.UserIdentifier` (signed-in `@aysoregion42.org` email, or `"System"`) to stamp `CreatedAt`/`CreatedBy`, `ModifiedAt`/`ModifiedBy`, and increment `RowVersion`.
  - **Granular Dirty Tracking**: Tracks dirty flags separately for the index, each `{year}/{slug}` tournament document, and each `{year}/{slug}/{division-key}` division document. Calling `SaveChangesAsync()` serializes and uploads **only** the documents that were actually modified during the request.
- **`IRepository<T>` / `Repository<T>`**:
  - Open-generic repository (`GetByIdAsync`, `GetAllAsync`, `FindAsync`, `AddAsync`, `Update`, `Remove`, `SaveChangesAsync`, `BeginTransactionAsync`) over `ITournamentDataContext` so Application services remain persistence-agnostic and straightforward to unit test.
  - **No separate Unit-of-Work class**: Transaction control lives directly on `IRepository<T>.BeginTransactionAsync()` returning `ITransaction`.

### In-Memory Snapshot Transactions (`ITransaction`)
- Because changes are buffered in `TournamentDataContext` until `SaveChangesAsync()` is called, `BeginTransactionAsync()` captures a deep clone snapshot of the in-memory collections, standings snapshots, dirty sets, and `_nextId`.
- If `RollbackAsync()` is called—or the `ITransaction` is disposed without `CommitAsync()`—the context restores the snapshot, discarding all partial entity mutations and dirty flags before anything reaches GCS or disk.

---

## 4. Concurrency & Locking

### Optimistic Concurrency (`Generation`)
- Every document read records its storage `Generation` (`StorageDocument<T>.Generation`).
- On `SaveChangesAsync()`, the store passes `expectedGeneration` (`UploadObjectOptions.IfGenerationMatch` in GCS, or `.generation` comparison locally):
  - **GCS**: If another request updated the object first (`412 PreconditionFailed`), the store retries up to 3 times where safe or throws `StorageConcurrencyException`; transient `429 TooManyRequests` responses back off and retry.
  - **LocalFile**: Throws `StorageConcurrencyException` immediately when `expectedGeneration` does not match the `.generation` sidecar value.

### Coarse-Grained Write Locks (`IStorageWriteLock`)
- Destructive or multi-file administrative operations (such as replacing a division schedule via CSV import using `StorageWriteLockNames.CsvScheduleImport`) acquire a TTL-bounded lock via `IStorageWriteLock.TryAcquireAsync(...)`.
- In GCS, lock creation uses `IfGenerationMatch = 0` so only one caller can create the lock object; expired locks are cleaned up using generation-matched deletes.

---

## 5. Configuration & Environments

| Setting | Default (`appsettings.json`) | Production (`Cloud Run`) | Description |
| :--- | :--- | :--- | :--- |
| `Storage:Provider` | `LocalFile` | `Gcs` | Selects `LocalFileTournamentDataStore` or `GcsTournamentDataStore`. |
| `Storage:BucketName` | `region42-storage` | `region42-storage` | Target GCS bucket when `Provider` is `Gcs`. |
| `Storage:CompetitionSlug` | `panther-shootout` | `panther-shootout` | Namespace folder isolating Panther Shootout objects in the shared bucket. |
| `Storage:LocalRoot` | `../../.local-data/storage` | *(unused)* | Local directory (solution-root `.local-data/storage`, gitignored) used when `Provider` is `LocalFile`. |

- **Local Development**: Works out of the box with zero cloud credentials; JSON files are written to `.local-data/storage` at the solution root for easy inspection.
- **Production (Google Cloud Run)**: Set `Storage__Provider=Gcs`, `Storage__BucketName=region42-storage`, and `Storage__CompetitionSlug=panther-shootout`. The Cloud Run service account requires the **Storage Object Admin** role on `gs://region42-storage`.
