# Shelfwise Project Status

Last reviewed: 2026-11-17

## Progress overview

The core library-management features and primary interfaces are implemented. Estimated implementation progress is **about 85%**; the remaining work is mainly account recovery verification, broader end-to-end testing, and production preparation.

## Implemented

- ASP.NET Core MVC application using .NET 10, EF Core 10, ASP.NET Core Identity, and Razor views.
- SQL Server support with existing migrations, plus SQLite development fallback and platform launch scripts.
- Admin and member portals with responsive Shelfwise styling.
- Book catalogue, category management, member registry, and profile/settings flows.
- Book issue, return, lost-book, overdue, fine, and payment workflows.
- Admin dashboard, reporting, CSV exports, and member borrowing history.
- SQL-translatable dashboard/reporting aggregates and SQL Server query checks.
- Dedicated Overdue Loans page with search, paging, real loan data, and circulation actions.
- Login now resolves by normalized email before verifying the selected Identity user.
- Local-only Admin diagnostic and password recovery commands; recovery requires Development, SQL Server, an explicit target confirmation, and the password environment variable.
- Seeded Admin/sample-member passwords are no longer hardcoded. Existing accounts and passwords are preserved.

## Current verification

- `dotnet build`: passed, 0 errors. Existing SQLite package `NU1903` advisory warning remains.
- `dotnet run -- --run-tests`: 27/27 passed.
- SQL Server diagnostic: connected to `localhost:1433`, database `LibraryManagementSystem`; found the Admin role/account and checked lockout, email, username, and member-profile state.
- Current Admin record is locked by Identity. `AccessFailedCount` is zero because Identity resets that counter when a lockout is applied. No linked Member profile exists.
- The required `SHELFWISE_ADMIN_RECOVERY_PASSWORD` was not available in the development environment, so the password reset and post-reset password check have **not** run. Do not treat Admin recovery as complete until that command succeeds.

## Remaining work

1. Set `SHELFWISE_ADMIN_RECOVERY_PASSWORD` and `SHELFWISE_CONFIRM_ADMIN_RECOVERY=admin@shelfwise.local`, then run `dotnet run -- --recover-admin-account` against the existing SQL Server database. The command verifies password, role, active state, lockout, and failure count without logging the password.
2. Restart the web app so it loads the updated login code, then manually verify Admin login and the Dashboard, overdue register, issue/return, and report flows.
3. Run broader browser checks for responsive layouts and authentication/authorization workflows.
4. Before production deployment, configure secrets and hosting-specific settings outside source control and perform a deployment review.

## Suggested teammate work

- **QA:** complete browser workflows and document reproducible issues in this file.
- **Frontend:** review desktop/tablet/mobile pages and keyboard accessibility.
- **Backend:** review reports, circulation edge cases, and SQL Server integration behavior.
- **Setup/docs:** validate Windows, macOS, and Linux setup using clean development databases.

Update this file when work is completed so the team has one shared progress checklist.
