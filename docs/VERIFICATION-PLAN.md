# Verification plan

## Current result

Not run: the .NET 10 SDK is not installed and the machine has no reachable SQL Server. JSON and project XML syntax were parsed successfully. The application has not been built, migrated, or started.

## After installing the SDK and configuring SQL Server

1. Run dotnet tool restore and dotnet restore.
2. Generate InitialCreate and inspect the migration for Identity tables, relationships, indexes, and check constraints.
3. Verify SQL connectivity with a SQL client before running dotnet ef database update.
4. Run dotnet build and resolve all compiler and Razor view errors.
5. Run dotnet run -- --initialize-database with a disposable admin email/password supplied through environment variables.
6. Run dotnet watch run over HTTPS and verify each workflow below.

## Manual acceptance checks

| Area | Check | Expected result |
|---|---|---|
| Authentication | Register valid member | Identity user and linked Member profile are created; password is stored only as an Identity hash |
| Authentication | Submit invalid login | Generic invalid-credentials message; no session |
| Authorization | Open an admin URL as a member | Access denied; no admin data is returned |
| Authorization | Deactivate a signed-in member | Next request signs out the member |
| Books | Add ISBN already in use | Validation error; database remains unchanged |
| Books | Update total quantity below active loans | Rejected; availability is unchanged |
| Books | Remove book with history | Archived; historical issue remains |
| Circulation | Issue a book with no available copies | Rejected; no issue row is created |
| Circulation | Issue to inactive member | Rejected; quantity does not change |
| Circulation | Issue normally | One active issue is recorded and available quantity decreases by one |
| Returns | Return active issue | Status/date/fine are saved and quantity increases once |
| Returns | Return the same issue again | Rejected; quantity remains unchanged |
| Fines | Return after due date | Fine equals overdue days multiplied by the stored issue-time rate |
| Fines | Record a payment above outstanding fine | Rejected; paid amount remains unchanged |
| Member data | View dashboard as a member | Only that member’s loans, history, and outstanding amounts appear |
| Dashboard | Compare totals with database rows | Counts and balances reflect persisted records |
| Persistence | Restart app after a transaction | Books and transaction history remain in SQL Server |

Record actual outcomes and any defects here after the SDK and database become available; do not mark checks passed based on source inspection alone.
