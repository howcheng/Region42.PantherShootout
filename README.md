# AYSO Region 42 — Panther Shootout Web Application

Web application for tracking schedules, game scores, pool-play standings, championship brackets, and Penalty Shootout (PSO) standings for the annual **Panther Shootout** youth soccer tournament hosted by AYSO Region 42 (Newbury Park, California).

This `README.md` serves as the central entry point and documentation index for both human developers and AI coding agents. Detailed domain rules and subsystem designs live in dedicated files under [`docs/`](docs/) so agents can load only the context relevant to a given task.

---

## Domain Overview

- **Competitions**: Each tournament year runs two parallel, independently scored competitions per division:
  1. **Pool Play & Championship Bracket ("the games")** — round-robin pool play scored under the AYSO 10-point system (with card deductions, forfeit rules, and a 6-step tiebreaker chain), followed by division-size-specific championship brackets (for 4-, 6-, 8-, and 12-team divisions).
  2. **Penalty Shootout ("PSO")** — a separate division-wide penalty-kick competition run alongside pool play (and opening bracket rounds in 6-team divisions), with its own rankings and tiebreakers.
- **Age & Gender Divisions**: Six divisions (`10U Boys`, `10U Girls`, `12U Boys`, `12U Girls`, `14U Boys`, `14U Girls`), also abbreviated as `10UB`, `10UG`, `12UB`, `12UG`, `14UB`, `14UG`. Each division contains 4, 5, 6, 8, 10, or 12 teams arranged into pools of 3, 4, or 5 teams.
- **Team Naming Convention**: Pool letter + 1-based team number (`Team.Code` / `Team.GetDesignation()`, e.g. `A1`, `C4`), paired with discrete `RegionNumber`, resolved `RegionName` (from year-independent `panther-shootout/regions.json`), and `CoachName` (e.g. `A1 Simi Valley: Wright`, `C2 Newbury Park: Oakman`).
- **Process Flow**:
  1. Before the tournament begins, staff maintain AYSO region mappings (`RegionNumber -> Name`), create/activate a tournament year, and import divisions, pools, teams, pool-play schedules, and championship bracket game times/fields via Excel (`.xlsx`) schedule upload.
  2. During the tournament, authenticated staff (`@aysoregion42.org`) enter game scores, cards, forfeits, PSO PK scores, and—only when formula tiebreakers are exhausted—live Kicks From The Mark (KFTM) tiebreaker overrides.
  3. Spectators view live pool standings, schedules, championship brackets, and PSO standings publicly without signing in.

---

## Technical Stack & Architecture

The solution uses **.NET 10 (ASP.NET Core MVC / C# 14)** organized into a **4-Layer Clean/Onion Architecture**:

### 1. Project Layering & Dependency Direction

`Domain` sits at the center with zero external dependencies; `Application` and `Infrastructure` each depend only on `Domain`; `Web` is the composition root referencing all three.

- **`Region42.PantherShootout.Domain`** (`src/Region42.PantherShootout.Domain`)
  - Core entities, enums, JSON document schemas (`Documents/`), entity/document mappers and path helpers (`Helpers/`), and persistence/service abstractions (`Interfaces/`). Zero external package dependencies.
- **`Region42.PantherShootout.Application`** (`src/Region42.PantherShootout.Application`)
  - Business services: pool-play standings and 6-tier tiebreaker calculations, championship bracket seeding/progression, PSO standings calculations, score entry orchestration, and two-step Excel (`.xlsx`) schedule import validation/execution. References `Domain` only.
- **`Region42.PantherShootout.Infrastructure`** (`src/Region42.PantherShootout.Infrastructure`)
  - JSON document persistence (local filesystem in development, Google Cloud Storage in production), coarse-grained storage write locks, scoped in-memory `TournamentDataContext` (dirty tracking + snapshot transactions), generic `Repository<T>`, and OpenXML `.xlsx` schedule workbook parsing. References `Domain` only.
- **`Region42.PantherShootout.Web`** (`src/Region42.PantherShootout.Web`)
  - ASP.NET Core MVC controllers and views, Google OAuth restricted to `@aysoregion42.org`, OWASP security headers + `'strict-dynamic'` nonce CSP (`CspNonceScriptTagHelper`), session-backed `TempData`, and DI wiring.
- **Test Projects** (`test/`)
  - xUnit + Moq test suites mirroring each layer (`Domain.Tests`, `Application.Tests`, `Infrastructure.Tests`, `Web.Tests`).

### 2. Hosting, Storage & CI/CD

- **Data Storage**: JSON documents in Google Cloud Storage (`gs://region42-storage` under the `panther-shootout` slug); mirrored on the local filesystem (`../../.local-data/storage`) during development. There is **no SQL/EF Core database**.
- **Authentication**: Google OAuth + application-level `DomainRequirement` restricting admin access to `@aysoregion42.org`.
- **Hosting & Deployment**: Dockerized multi-stage container deployed to **Google Cloud Run** via GitHub Actions.

---

## Related Applications

- **`Region42.ScoresStandings`** (`../Region42.ScoresStandings`) is the sibling AYSO Region 42 regular-season scores and standings web application. Both applications share the same architectural patterns, GCS bucket (`gs://region42-storage`), Google OAuth domain restriction setup, and Cloud Run deployment model. Consult it when implementing analogous infrastructure or UI patterns; shared components may eventually be extracted into a common library.

---

## Documentation Index

Load the specific guides below as needed when working on related features:

### Architecture & Infrastructure
- **[Site Setup, Authentication & Security Guide](docs/site-setup.md)** — Local development quick start, Google OAuth + `@aysoregion42.org` `AdminPolicy` setup, MVC middleware pipeline (forwarded headers, session `TempData`, theme cookie), `'strict-dynamic'` nonce CSP + `CspNonceScriptTagHelper`, and Cloud Run configuration.
- **[JSON File Storage Strategy](docs/json-storage.md)** — Bucket layout and `ScoresStandings` coexistence rules, JSON document schemas, `ITournamentDataContext` dirty-tracking and snapshot transactions, optimistic concurrency (`Generation`), write locks, and configuration.

### Business Rules & Feature Specifications
- **[Tournament Business Rules](docs/BusinessRules.md)** — Complete specification of division size configurations (4/5/6/8/10/12 teams), AYSO 10-point pool-play scoring, forfeits, yellow/red cards, the 6-step pool tiebreaker order, championship brackets (including 10-team combined standings and 12-team pool-winner/runner-up rankings), PSO shootout rules (including 5-team bye caps and 6-team Round 4 bracket shootouts), and KFTM overrides.
- **[Pool-Play Standings Calculation](docs/pool-play-standings.md)** — Application-layer implementation map for the AYSO 10-point calculator, uncapped vs. capped goal stats, 6-step tiebreakers, 10-team combined / 12-team winner–runner-up rankings, KFTM detection, and standings snapshot shape.
- **[Championship Brackets](docs/championship-brackets.md)** — Bracket topologies by division size, standings-based seeding, 6-team semifinal progression, no-bracket champions (5/10), 12-team winner/runner-up slots, and PSO Round 4 pairing sync.
- **[PSO Standings Calculation](docs/pso-standings.md)** — Division-wide penalty-shootout ranking, 3- vs 4-round schedules, 5-team 4th-round cap, PK goals-against / PSO KFTM tiebreakers, and PSO snapshot shape.
- **[Score Entry & Standings Refresh](docs/score-entry-and-refresh.md)** — Admin score/KFTM update validation, refresh pipeline (pool → bracket → PSO → snapshot), and `saveChanges` transaction behavior.
- **[Excel Schedule Import & AYSO Regions](docs/excel-schedule-import.md)** — Two-step `.xlsx` workbook import (`Pools` + `Schedule` tabs), discrete `Team` fields, year-independent `regions.json` catalog, year-agnostic bracket heuristics, and preview cell overrides.
