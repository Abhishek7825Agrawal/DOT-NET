# Phase 7A verification record

## Automated verification

Run with `dotnet run --no-build -- --run-tests` on .NET 10:

| Suite | Passed | Failed |
|---|---:|---:|
| Core business rules | 26 | 0 |
| SQL Server query translation | 5 | 0 |
| Excel import and security | 27 | 0 |
| Phase 5 digital library | 32 | 0 |
| Phase 6 marketplace | 39 | 0 |
| Phase 7A activity and audit | 6 | 0 |
| Phase 7A password recovery | 9 | 0 |
| **Total** | **144** | **0** |

The Phase 7A tests cover activity/security table separation, per-user query isolation, correlation IDs, append-only enforcement, retention, Identity password policy, valid/expired/reused reset tokens, password persistence after reopening the database, password/hash-free admin view models, and preservation of an existing Admin password across a second startup seed.

`dotnet build --no-restore` succeeds with zero errors. It reports the existing `SQLitePCLRaw.lib.e_sqlite3` NU1903 advisory and two unused marketplace storage constructor parameters.

## Application smoke checks

- Started the MVC app against a disposable SQLite database.
- Login and Forgot Password pages returned HTTP 200.
- Forgot Password rendered an anti-forgery token; posting an unknown email returned the same generic confirmation page (HTTP 200).
- Simulated an older SQLite database by removing the new column and audit tables, then initialized it. Startup added `AspNetUsers.IsActive` and both audit tables; all 8 existing book rows remained intact.
- Generated an EF Core SQL Server migration at `Migrations/20261120024415_AddPasswordRecoveryAndAuditLogs.cs`. It was generated but not applied to a production SQL Server.

## Theme and screenshot checks

Captured the login page with Firefox headless using system-theme light/dark profiles at 1280×800, 768×1024, and 375×812. These are responsive login-page captures, not a Chrome DevTools run or a full dashboard visual audit. Initial captures exposed mismatched hardcoded login-card and footer backgrounds; both now use theme-aware surfaces.

Core specified text pairs calculate to 15.8:1 (light primary), 8.2:1 (light secondary), 15.4:1 (dark primary), and 9.1:1 (dark secondary). This calculation covers the central palette pairs; it does not establish WCAG compliance for every legacy component or image/chart.

Screenshots: `artifacts/phase7a/`.

## Remaining manual/provider checks

- Configure a local SMTP catcher and verify provider acceptance and delivery. No SMTP provider was configured during this run.
- Apply the SQL Server migration to a disposable SQL Server 2022 database and run the app against it.
- Run Chrome DevTools at the requested viewports across every dashboard, modal, table, reader, and marketplace screen. Chrome was not installed in this environment; Firefox screenshots only were available.
- The existing marketplace leaves paid orders pending without a payment gateway and has no refund workflow. Those unavailable operations cannot produce completion/refund events until the corresponding business workflows exist.
