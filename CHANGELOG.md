# Changelog

All notable changes to this project are documented here. Format based on [Keep a Changelog](https://keepachangelog.com/), versioning follows [SemVer](https://semver.org/).

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
