# Site Setup, Authentication, Security & Deployment Guide

This guide covers local development setup, ASP.NET Core MVC pipeline configuration, `@aysoregion42.org` Google OAuth setup, Content Security Policy (`'strict-dynamic'` + automatic `<script>` nonces), Docker containerization, and Google Cloud Run CI/CD deployment for `Region42.PantherShootout.Web`.

---

## 1. Local Development Quick Start

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- *(Optional, for testing admin sign-in locally)* Google OAuth 2.0 Web Client credentials from GCP project `ayso-region-42`

### Run the Web Application

```powershell
cd src/Region42.PantherShootout.Web
dotnet run --launch-profile https
```

Default local URLs (from `Properties/launchSettings.json`):
- **HTTPS:** `https://localhost:7133`
- **HTTP:** `http://localhost:5243`

Public pages work immediately without Google OAuth credentials configured in `Development` (unconfigured credentials use safe placeholder strings locally, whereas non-Development environments fail fast at startup if `Authentication:Google:ClientId` or `ClientSecret` is missing).

### Local JSON Storage
No database setup is required. By default (`appsettings.json`), `Storage:Provider` is `LocalFile` and writes JSON documents and `.generation` concurrency sidecar files to `../../.local-data/storage` (`.local-data/storage` at the solution root, ignored by `.gitignore`), mirroring the exact `gs://region42-storage` object layout (`panther-shootout/index.json`, `panther-shootout/regions.json`, `{year}/panther-shootout/...`). See [JSON File Storage Strategy](json-storage.md) for full details.

### Run Tests

```powershell
dotnet test
```

---

## 2. Google OAuth & `@aysoregion42.org` Authorization

Administrative routes are protected by two defense-in-depth layers matching `Region42.ScoresStandings`:

1. **Google Cloud OAuth Consent Screen (`Internal`)**: Blocks non-`@aysoregion42.org` Google Workspace accounts at Google's sign-in screen.
2. **Application-Level `AdminPolicy` (`DomainRequirement`)**: Verifies in `DomainRequirementHandler` (`src/Region42.PantherShootout.Web/Authorization/DomainRequirement.cs`) that the authenticated user has a `ClaimTypes.Email` (or `"email"`) claim ending in `@aysoregion42.org` (case-insensitive).

### Configure Google OAuth Credentials

1. Open the [GCP API Credentials Console](https://console.cloud.google.com/apis/credentials?project=ayso-region-42).
2. Create or select the **OAuth 2.0 Web Client ID** for Panther Shootout (or add the local/Cloud Run redirect URIs to the Region 42 web client).
3. Ensure **Authorized redirect URIs** include:
   - Local HTTPS: `https://localhost:7133/signin-google`
   - Local HTTP: `http://localhost:5243/signin-google`
   - Production: `https://<your-cloud-run-domain>/signin-google`
4. Store the client ID and secret in local .NET User Secrets (`UserSecretsId` is already configured in `Region42.PantherShootout.Web.csproj`):

```powershell
cd src/Region42.PantherShootout.Web
dotnet user-secrets set "Authentication:Google:ClientId" "YOUR_CLIENT_ID.apps.googleusercontent.com"
dotnet user-secrets set "Authentication:Google:ClientSecret" "YOUR_CLIENT_SECRET"
```

### Auth Flows & Audit Identity
- **Sign-In / Sign-Out (`AccountController`)**:
  - `GET /Account/Login?returnUrl=...` challenges the `"Google"` scheme and validates `returnUrl` with `Url.IsLocalUrl(...)` before redirecting (falling back to `HomeController.Index` to prevent open redirects).
  - `GET /Account/Logout` signs out of `CookieAuthenticationDefaults.AuthenticationScheme` and redirects home.
  - `GET /Account/AccessDenied` returns HTTP `403 Forbidden` when a signed-in account fails `AdminPolicy`.
- **Audit Stamping (`CurrentUserService`)**:
  - Implements `Domain.Interfaces.ICurrentUserService` (`src/Region42.PantherShootout.Web/Services/CurrentUserService.cs`), resolving `ClaimTypes.Email` -> `"email"` -> `Identity.Name` -> `"System"` so `TournamentDataContext` stamps `CreatedBy` / `ModifiedBy` without coupling `Domain` or `Infrastructure` to `HttpContext`.

---

## 3. Web Pipeline, Session TempData & Theme Persistence

`Program.cs` (`src/Region42.PantherShootout.Web/Program.cs`) configures the ASP.NET Core MVC pipeline in the following order:

1. **`UseForwardedHeaders()`**: Honors `X-Forwarded-For` and `X-Forwarded-Proto` (with cleared `KnownIPNetworks` / `KnownProxies`) so HTTPS scheme detection and Google OAuth redirect URIs resolve to `https://` behind Google Cloud Run's load balancer.
2. **`UseExceptionHandler("/Home/Error")` & `UseHsts()`** *(non-Development)*: Configures 1-year OWASP HSTS (`max-age=31536000; includeSubDomains; preload`).
3. **`UseHttpsRedirection()`**
4. **`UseSecurityHeaders()`**: Emits OWASP response headers and the per-request nonce CSP (see Section 4).
5. **`UseRouting()`**
6. **`UseThemeCookie()`**: `ThemeCookieMiddleware` (`Middleware/ThemeCookieMiddleware.cs`) refreshes the `region42_theme` (`light` / `dark`) cookie with a 400-day sliding expiration on each visit; `_Layout.cshtml` reads the cookie onto `<html data-bs-theme="...">` on the server to prevent flash-of-wrong-theme, and `wwwroot/js/site.js` toggles it client-side.
7. **`UseSession()`**: In-memory session backing `SessionStateTempDataProvider` (`ITempDataProvider`) so large two-step Excel schedule import validation/preview payloads stored in `TempData` never hit browser cookie size limits (`HTTP 431`).
8. **`UseAuthentication()` & `UseAuthorization()`**
9. **`MapStaticAssets()` & MVC Controller Routes** (`{controller=Home}/{action=Index}/{id?}`).

---

## 4. Security Headers & `'strict-dynamic'` Content Security Policy

### OWASP Headers (`SecurityHeadersMiddleware`)
Every HTTP response includes:
- `Content-Security-Policy`:
  - `default-src 'self'`
  - `script-src 'nonce-{base64-nonce}' 'strict-dynamic'`
  - `object-src 'none'`
  - `style-src 'self'`
  - `img-src 'self' data:`
  - `font-src 'self'`
  - `connect-src 'self'` *(expanded with `ws://localhost:* wss://localhost:* http://localhost:* https://localhost:*` in `Development` for Visual Studio Browser Link / Hot Reload)*
  - `frame-ancestors 'none'`
  - `base-uri 'self'`
  - `form-action 'self'`
- `X-Frame-Options: DENY`
- `X-Content-Type-Options: nosniff`
- `Referrer-Policy: strict-origin-when-cross-origin`
- `Permissions-Policy: camera=(), microphone=(), geolocation=()` *(plus `unload=*` in `Development`)*
- `Cross-Origin-Opener-Policy: same-origin`
- `Cross-Origin-Resource-Policy: same-origin`

### Automatic `<script>` Nonces (`ICspNonceService` + `CspNonceScriptTagHelper`)
Because `script-src` uses `'strict-dynamic'` instead of `'self'`, host-allowlist paths are ignored by modern browsers and **every** rendered `<script>` tag must carry the current request's cryptographic nonce:

- **`ICspNonceService` / `CspNonceService`** (`Services/CspNonceService.cs`): Scoped per request (and cached in `HttpContext.Items["Region42.CspNonce"]`), generating a 32-byte (256-bit) `RandomNumberGenerator` Base64 nonce once per request.
- **`CspNonceScriptTagHelper`** (`TagHelpers/CspNonceScriptTagHelper.cs`, registered globally in `Views/_ViewImports.cshtml`): Targets all Razor `<script>` elements with `Order = 100` (after built-in `UrlResolutionTagHelper`, `ScriptTagHelper`, and `ImportMapTagHelper`) and automatically stamps `nonce="{nonce}"` onto rendered `<script>` tags.
  - Inline styles (`style="..."`) and inline event handlers (`onclick="..."`) remain blocked by CSP—keep all styles in CSS files under `wwwroot/css/` and all page scripts in external IIFE files under `wwwroot/js/` loaded via standard `<script src="~/js/..." asp-append-version="true"></script>` tags.
  - To opt a specific tag out of automatic nonce injection (if ever needed), add `asp-add-nonce="false"`.

---

## 5. Production Environment Settings (Google Cloud Run)

In `Production` (`appsettings.Production.json` + Cloud Run environment variables / Secret Manager):

| Configuration Key / Env Var | Production Value | Notes |
| :--- | :--- | :--- |
| `ASPNETCORE_ENVIRONMENT` | `Production` | Enforces HSTS, `/Home/Error`, strict CSP `connect-src`, and required OAuth config. |
| `Storage__Provider` | `Gcs` | Uses `GcsTournamentDataStore` and `GcsStorageWriteLock`. |
| `Storage__BucketName` | `region42-storage` | Shared Region 42 GCS bucket (requires `Storage Object Admin` on `region42-web-app@ayso-region-42.iam.gserviceaccount.com`). |
| `Storage__CompetitionSlug` | `panther-shootout` | Isolates Panther Shootout data from `ScoresStandings` (`core-season`). |
| `Authentication__Google__ClientId` | Secret `panther-shootout-google-oauth-client-id:latest` | Client ID from the **Panther Shootout (deployed)** OAuth 2.0 Web Client. |
| `Authentication__Google__ClientSecret` | Secret `panther-shootout-google-oauth-client-secret:latest` | Client Secret from the **Panther Shootout (deployed)** OAuth 2.0 Web Client. |

---

## 6. Docker & GitHub Actions CI/CD Deployment

### Container Build & Local Smoke Test

The solution-root [`Dockerfile`](../Dockerfile) builds `Region42.PantherShootout.Web` using a multi-stage `.NET 10` image (`mcr.microsoft.com/dotnet/sdk:10.0` -> `mcr.microsoft.com/dotnet/aspnet:10.0`), exposes port `8080`, and runs as the built-in non-root `app` user. [`.dockerignore`](../.dockerignore) excludes build outputs, IDE/agent folders, `test/`, `docs/`, `.local-data/`, and local `appsettings.*.json` overrides.

To build and smoke-test the container locally (using `Development` mode + container-local filesystem storage so no GCP credentials are required):

```powershell
docker build -t region42-panther-shootout:local .

docker run --rm -p 8080:8080 `
  -e ASPNETCORE_ENVIRONMENT=Development `
  -e Storage__Provider=LocalFile `
  -e Storage__LocalRoot=/tmp/panther-shootout-storage `
  region42-panther-shootout:local
```

Then browse to `http://localhost:8080`.

### One-Time GCP & GitHub Repository Setup (`ayso-region-42`)

Before running the production deployment workflow for the first time, complete these one-time steps:

1. **Store `"Panther Shootout (deployed)"` OAuth credentials in Secret Manager**:
   Copy the Client ID and Client Secret from the **Panther Shootout (deployed)** OAuth 2.0 Web Client in the [GCP API Credentials Console](https://console.cloud.google.com/apis/credentials?project=ayso-region-42) into Secret Manager (`printf` / `Write-Host -NoNewline` avoids trailing newlines):

   ```powershell
   # PowerShell (avoid trailing CRLF in secret payloads)
   [System.IO.File]::WriteAllText("$env:TEMP\pso-client-id.txt", "YOUR_CLIENT_ID.apps.googleusercontent.com")
   gcloud secrets create panther-shootout-google-oauth-client-id `
     --project=ayso-region-42 `
     --replication-policy=automatic `
     --data-file="$env:TEMP\pso-client-id.txt"
   Remove-Item "$env:TEMP\pso-client-id.txt"

   [System.IO.File]::WriteAllText("$env:TEMP\pso-client-secret.txt", "YOUR_CLIENT_SECRET")
   gcloud secrets create panther-shootout-google-oauth-client-secret `
     --project=ayso-region-42 `
     --replication-policy=automatic `
     --data-file="$env:TEMP\pso-client-secret.txt"
   Remove-Item "$env:TEMP\pso-client-secret.txt"
   ```

   *(Note: Runtime service account `region42-web-app@ayso-region-42.iam.gserviceaccount.com` already has project-level Secret Manager Secret Accessor and Storage Object Admin on `gs://region42-storage`.)*

2. **Configure GitHub Actions GCP authentication secrets** on `howcheng/Region42.PantherShootout` (`Settings -> Secrets and variables -> Actions`):
   - **Option A (Service Account Key)**: Set `GCP_SA_KEY` to the JSON key for `github-actions-deployer@ayso-region-42.iam.gserviceaccount.com` (matching `Region42.ScoresStandings`).
   - **Option B (Workload Identity Federation)**: Set `GCP_WIF_PROVIDER` and `GCP_WIF_SERVICE_ACCOUNT`.

3. **Authorize the Cloud Run Redirect URI**:
   After the initial Cloud Run deployment creates the `region42-panther-shootout` service URL (and any custom domain mapping), add `https://<your-cloud-run-domain>/signin-google` to **Authorized redirect URIs** on the **Panther Shootout (deployed)** OAuth 2.0 Web Client in GCP Console.

### GitHub Actions Workflows

- **Pull Request CI ([`.github/workflows/ci.yml`](../.github/workflows/ci.yml))**:
  - Runs on every `pull_request` targeting `main`.
  - Restores NuGet packages (with caching), builds the solution in `Release`, executes all unit test suites (`Domain.Tests`, `Application.Tests`, `Infrastructure.Tests`, `Web.Tests`), and uploads `.trx` test result artifacts.
- **Cloud Run Deployment ([`.github/workflows/deploy.yml`](../.github/workflows/deploy.yml))**:
  - Runs automatically on `push` to `main` or manually via `workflow_dispatch`.
  - Executes the full `build-and-test` job first; only when all tests pass does the `deploy` job build and push container tags (`:<commit-sha>` and `:latest`) to Artifact Registry (`us-west2-docker.pkg.dev/ayso-region-42/region42/region42-panther-shootout`) and deploy revision updates to Cloud Run service `region42-panther-shootout` (`us-central1`, `0–5` instances, `512Mi` RAM, `1` vCPU, running as `region42-web-app@ayso-region-42.iam.gserviceaccount.com`).
