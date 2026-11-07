# Changelog

All notable changes to this project are documented here. Format based on [Keep a Changelog](https://keepachangelog.com/), versioning follows [SemVer](https://semver.org/).

## [1.1.0] - 2026-11-08

Architecture review release; see [ARCHITECTURE_REVIEW.md](ARCHITECTURE_REVIEW.md) for findings and rationale.

### Added
- **Roles**: `Admin` and `Customer` (JWT role claim, `role` in auth responses). Only Admins can create, update or delete services; public registration always creates Customers. Demo admin `admin@bookit.test` is seeded in Development; production admins are provisioned via `Seed:AdminEmail` / `Seed:AdminPassword`.
- Minimal admin UI in the frontend (add / edit / delete service).
- **EF Core migrations** (`InitialCreate`, `AddBookingOverlapGuard`) applied automatically at startup; `dotnet-ef` pinned in `.config/dotnet-tools.json`.
- **Database-level double-booking protection**: SQLite triggers reject overlapping non-cancelled bookings of the same service on insert and update.
- Global exception handling with RFC 7807 ProblemDetails (typed application exceptions), stricter request validation, and indexes.
- `/health` (with database check) and `/health/live` endpoints.
- Per-IP rate limiting on `/api/auth/*`, security headers incl. a strict Content-Security-Policy, explicit CORS policy (closed unless `Cors:AllowedOrigins` is set), HSTS outside Development.
- Options pattern with startup validation (`Jwt`, `Seed`, `RateLimiting`, `Cors`); `UserSecretsId` for local secrets.
- Structured logging in use-case services; `TimeProvider` for testable time.
- 39 new tests (60 total): roles, DB triggers, parallel booking races, concurrent payment, ProblemDetails/validation, health, headers, rate limiting, startup configuration.

### Changed
- Code reorganised into `Domain` / `Application` / `Infrastructure` / `Web` folders, one type per file, thin controllers, use-case services behind interfaces (`IAppDbContext`, `ITokenService`).
- **API contract:** error bodies are now ProblemDetails (`detail`, `errors`) instead of `{ "error": ... }`; `/api/services` returns a `ServiceDto`; `?includeInactive=true` is honoured for Admins only.
- Payment, invoice creation and registration are race-safe (conditional update / unique indexes mapped to `409`).
- Version bumped to 1.1.0.

### Removed
- In-process `SemaphoreSlim` booking lock (superseded by the database guard).
- Hardcoded JWT key from `appsettings.json`; the app now fails fast without a valid `Jwt:Key` (the committed dev-only key works in Development only).

### Upgrade notes
- Databases created by 1.0.0 (`EnsureCreated`) have no migration history or `Role` column: delete the old `bookit.db` and let 1.1.0 recreate it.
- Set `Jwt__Key` (at least 32 chars) in any non-Development environment.

## [1.0.0] - 2026-11-07

First stable release.

### Added
- ASP.NET Core 8 Web API with EF Core + SQLite; schema auto-created and demo data seeded on startup.
- Registration and login with ASP.NET Identity `PasswordHasher` and JWT bearer authentication.
- Services CRUD, bookings (create, reschedule, cancel, delete) and per-user data isolation.
- Double-booking prevention: overlapping slots for the same service return `409`; back-to-back slots and cancelled bookings are allowed.
- Invoice generation from a booking (service line plus 10% tax, rounded to cents) and a **mock** payment endpoint (`402` on simulated decline, `409` if already paid).
- Swagger UI in Development.
- Frontend (plain HTML/CSS/JS, no build step):
  - light and dark themes (`prefers-color-scheme`), responsive down to phone width;
  - navigation, date/time picker with live end-time preview, status badges, invoice detail dialog with line items;
  - loading skeletons, empty states, inline form validation, friendly toasts for `409` (slot taken) and `402` (payment declined);
  - session token persisted in `localStorage` (guarded with try/catch), logout;
  - accessibility: labelled fields, visible focus styles, skip link, native `<dialog>` modals, reduced-motion support.
- xUnit test suite (21 tests): password hashing and login, double-booking prevention, invoice totals, and API flows via `WebApplicationFactory` with in-memory SQLite.
- README with architecture diagram, API reference and screenshots.

### Known limitations
- No user roles: any authenticated user can manage services.
- Booking conflict check is serialised in-process; multi-instance deployments need a database-level constraint.
- Schema is created with `EnsureCreated` (no EF migrations).
