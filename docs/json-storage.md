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
    regions.json
    locks/
      schedule-import.json
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

All object paths and division keys are generated centrally by `DivisionKeyHelper` (`src/Region42.PantherShootout.Domain/Helpers/DivisionKeyHelper.cs`), and `ITournamentDataStore.ListTournamentPathsAsync()` filters strictly for `*/tournament.json` objects (ignoring `index.json` and `*/season.json`).

---

## 2. Document Schemas & Responsibilities

Document POCOs live in `src/Region42.PantherShootout.Domain/Documents/` and are mapped to/from domain entities via `DocumentEntityMapper`.

### `panther-shootout/index.json` (`TournamentIndexDocument`)
- Top-level catalog of all Panther Shootout tournament years in the bucket.
- Stores `schemaVersion` and `tournaments[]` (`tournamentYearId`, `year`, `slug`, `name`, `isActive`, `tournamentPath`).

### `panther-shootout/regions.json` (`RegionsDocument`)
- Year-independent catalog mapping permanent AYSO region numbers to display names (e.g. `42` -> `"Newbury Park"`, `121` -> `"Simi Valley"`).
- Hydrated into `ITournamentDataContext.Regions` (with default regions seeded in memory until first save) and wired onto `Team.RegionName` across all tournament years.

### `{year}/panther-shootout/tournament.json` (`TournamentDocument`)
- Authoritative metadata and roster hierarchy for a single tournament year.
- Contains:
  - `schemaVersion`, `version`, `modifiedAt`, `modifiedBy`
  - `tournament`: `TournamentYear` metadata (`id`, `year`, `name`, `isActive`, `customMessage`)
  - `divisions[]`: division config (`id`, `key`, `name`, `ageGroup`, `gender`, `size`, `customMessage`) -> `pools[]` (`id`, `name`) -> `teams[]` (`id`, `teamNumber`, `regionNumber`, `coachName`, `name`)
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

## 3. Data Access Architecture (`Infrastructure`)

All concrete storage and persistence implementations live in `src/Region42.PantherShootout.Infrastructure` and are registered via `IServiceCollection.AddInfrastructure(IConfiguration)` (`DependencyInjection/ServiceCollectionExtensions.cs`), with zero ASP.NET Core or EF Core dependencies.

### `TournamentJsonSerializer` & `ITournamentDataStore` (Low-Level Object I/O)
- **`TournamentJsonSerializer`** (`Storage/TournamentJsonSerializer.cs`): Serializes and deserializes documents using camelCase property names, camelCase string enums, `WhenWritingNull` omission, indented formatting, and `UTF8Encoding(encoderShouldEmitUTF8Identifier: false)` (UTF-8 **without** BOM).
- **`GcsTournamentDataStore`** (`Storage/GcsTournamentDataStore.cs`, singleton): Reads/writes JSON objects in GCS using `Google.Cloud.Storage.V1.StorageClient` and wraps payloads in `StorageDocument<T>` carrying the GCS object `Generation`. Translates GCS `404 NotFound` responses into `FileNotFoundException`.
- **`LocalFileTournamentDataStore`** (`Storage/LocalFileTournamentDataStore.cs`, singleton): Mirrors the exact bucket directory structure on local disk under `Storage:LocalRoot`. Writes atomically via a unique `.<guid>.tmp` file move and tracks simulated object generations in a `<filename>.generation` sidecar file so concurrency semantics (`expectedGeneration: null`, `0` for create-only, or `> 0` for exact match) behave identically in local development and tests.

### `ITournamentDataContext` & `IRepository<T>` (Scoped Unit of Work & CRUD)
- **`TournamentDataContext`** (`Storage/TournamentDataContext.cs`) is registered as **scoped** per HTTP request:
  - **Lazy Hydration & Navigation Wiring**: Lazily loads `panther-shootout/index.json`, `tournament.json` files, and `{division-key}.json` files into in-memory collections without marking anything dirty on read, and wires bidirectional navigation properties (`TournamentYear` <-> `Division` <-> `Pool` <-> `Team`, `PoolPlayGame`/`ChampionshipGame` <-> `ShootoutRound`, `TiebreakerOverride`). Missing `{division-key}.json` files hydrate cleanly as empty `DivisionDataDocument` instances until games/scores are saved.
  - **ID Allocation**: Computes `_nextId = max(existing entity IDs) + 1` across all hydrated entity types and assigns sequential integer IDs when `entity.Id == 0` (or via `AllocateNextId()`).
  - **Audit Stamping**: Uses `ICurrentUserService.UserIdentifier` (signed-in `@aysoregion42.org` email, or `"System"`) to stamp `CreatedAt`/`CreatedBy`, `ModifiedAt`/`ModifiedBy`, and increment `RowVersion` on `Add`/`Update`, while incrementing the document-level `Version` when building dirty `TournamentDocument` and `DivisionDataDocument` payloads.
  - **Granular Dirty Tracking & Cascading Removals**: Tracks dirty flags separately for the index (`_indexDirty`), each `(Year, Slug)` tournament document, and each `(Year, Slug, DivisionKey)` division document. Removing a `TournamentYear`, `Division`, or `Pool` cascades in-memory cleanup to child entities and clears stale dirty flags so `SaveChangesAsync()` serializes and uploads **only** the live documents modified during the request, refreshing stored `Generation` tokens after each write.
- **`IRepository<T>` / `Repository<T>`** (`Repositories/Repository.cs`):
  - Open-generic repository (`GetByIdAsync`, `GetAllAsync`, `FindAsync`, `AddAsync`, `Update`, `Remove`, `SaveChangesAsync`, `BeginTransactionAsync`) over `ITournamentDataContext` so Application services remain persistence-agnostic and straightforward to unit test.
  - **No separate Unit-of-Work class**: Transaction control lives directly on `IRepository<T>.BeginTransactionAsync()` returning `ITransaction`.

### In-Memory Snapshot Transactions (`ITransaction`)
- Because mutations are buffered in `TournamentDataContext` until `SaveChangesAsync()` is called, `BeginTransactionAsync()` captures a deep-cloned `JsonDataTransactionSnapshot` of all in-memory entity collections, standings snapshots, index entries, path maps, dirty sets, and `_nextId` (guarding against nested transactions on the same context).
- If `RollbackAsync()` is called—or the `ITransaction` is disposed without `CommitAsync()`—the context restores the snapshot and re-wires navigation properties, discarding all partial entity mutations and dirty flags before anything reaches GCS or disk.

---

## 4. Concurrency & Locking

### Optimistic Concurrency (`Generation`)
- Every document read records its storage `Generation` (`StorageDocument<T>.Generation`).
- On `SaveChangesAsync()`, the store passes `expectedGeneration` (`UploadObjectOptions.IfGenerationMatch` in GCS, or `.generation` sidecar comparison locally):
  - **GCS**: When an explicit `expectedGeneration` is supplied and another request updated the object first (`412 PreconditionFailed`), `GcsTournamentDataStore` throws `StorageConcurrencyException` immediately so callers never overwrite newer changes silently. Unconditional writes (`expectedGeneration: null`) retry up to 3 times on `412`, and transient `429 TooManyRequests` / `503 ServiceUnavailable` responses back off and retry.
  - **LocalFile**: Throws `StorageConcurrencyException` immediately when `expectedGeneration` does not match the `.generation` sidecar value (including `expectedGeneration = 0` when the file already exists).

### Coarse-Grained Write Locks (`IStorageWriteLock`)
- Destructive or multi-file administrative operations (such as replacing a division schedule via CSV import using `StorageWriteLockNames.CsvScheduleImport`) acquire a TTL-bounded lock via `IStorageWriteLock.TryAcquireAsync(...)`, returning an `IStorageWriteLockHandle` (`IAsyncDisposable`).
- **`GcsStorageWriteLock`** (`Storage/GcsStorageWriteLock.cs`): Creates lock objects atomically with `UploadObjectOptions.IfGenerationMatch = 0` so only one caller can win a race; expired locks and `DisposeAsync()` releases delete the lock object using `DeleteObjectOptions.IfGenerationMatch` pinned to the acquired generation so a late/expired release can never delete a newer holder's active lock.
- **`LocalFileStorageWriteLock`** (`Storage/LocalFileStorageWriteLock.cs`): Writes lock files atomically under `panther-shootout/locks/{lockName}.json` and verifies `Holder` + `AcquiredAtUtc` before deleting on `DisposeAsync()` to prevent clobbering reclaimed locks after TTL expiry.

---

## 5. Configuration & Environments

`StorageOptions` (`Storage/StorageOptions.cs`) binds to the `"Storage"` configuration section:

| Setting | Default (`StorageOptions`) | Production (`Cloud Run`) | Description |
| :--- | :--- | :--- | :--- |
| `Storage:Provider` | `LocalFile` | `Gcs` | Selects `LocalFileTournamentDataStore` + `LocalFileStorageWriteLock` or `GcsTournamentDataStore` + `GcsStorageWriteLock` (registering `StorageClient` via `TryAddSingleton`). |
| `Storage:BucketName` | `region42-storage` | `region42-storage` | Target GCS bucket when `Provider` is `Gcs`. |
| `Storage:CompetitionSlug` | `panther-shootout` | `panther-shootout` | Namespace folder isolating Panther Shootout objects in the shared bucket. |
| `Storage:LocalRoot` | `../../.local-data/storage` | *(unused)* | Local directory (solution-root `.local-data/storage`, gitignored) used when `Provider` is `LocalFile`. |

- **Local Development**: Works out of the box with zero cloud credentials; JSON files and `.generation` sidecars are written to `.local-data/storage` at the solution root for easy inspection.
- **Production (Google Cloud Run)**: Set `Storage__Provider=Gcs`, `Storage__BucketName=region42-storage`, and `Storage__CompetitionSlug=panther-shootout`. The Cloud Run service account requires the **Storage Object Admin** role on `gs://region42-storage`.
