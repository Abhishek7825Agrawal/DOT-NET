# Shelfwise Library Management System — Enterprise Edition

An enterprise-grade ASP.NET Core MVC web application for managing library catalogues, member accounts, circulation, overdue fines, and reporting analytics. Targets .NET 10 LTS, C# 14, EF Core 10, and SQL Server 2022 on Arch Linux x86_64.

## Quick Start (Mac, Linux, Windows)

Install the **.NET 10 SDK**, then run the launcher for your operating system from the project folder:

- **Windows:** double-click `start.bat`.
- **macOS / Linux:** run `./start.sh` in Terminal. If needed, make it executable once with `chmod +x start.sh`.

Both launchers initialize the existing database in Development mode and start the site at <http://localhost:5180>. If `ConnectionStrings__DefaultConnection` is supplied in the environment, they use SQL Server; otherwise they use the local SQLite fallback (`library.db`). If Shelfwise is already running, the launcher reuses that instance instead of trying to bind the same port again. Stop the app with `Ctrl+C` in the terminal.

On a fresh database, set `LIBRARY_ADMIN_PASSWORD` before starting the app to create the initial Admin account. The default email is `admin@shelfwise.local`. Existing Admin passwords are never replaced by startup initialization. Sample member creation is optional and requires `LIBRARY_SAMPLE_MEMBER_PASSWORD`.

To start manually instead, run `dotnet run` from the project folder. The launcher files are convenient when local user-secrets or environment variables select a different database provider.

---

## 1. System Architecture

The application implements a high-performance **Layered Monolith**:
- **Controllers & Razor Views:** Handles HTTP requests, model state binding, validation, and renders accessible Bootstrap 5.3 UI templates.
- **Service Layer:**
  - `BookService`: Title creation, update, safe archive/restore, and duplicate ISBN checks.
  - `CategoryService`: Genre taxonomy, title counts, and cascading category synchronization.
  - `CirculationService`: Issue loans, return processing, lost title settlement, and fine payments with `RowVersion` optimistic concurrency.
  - `FineCalculator`: Centralized calculation of overdue days, grace periods, daily rates, and penalty fees.
  - `SettingsService`: Configurable library policy (loan period, fine rate, borrowing quotas, grace periods, timezone).
  - `ReportingService`: Metrics, 7-day circulation trend analysis, top borrower/book leaderboards, and CSV exports.
  - `MemberService`: Borrower search, activation, and suspension.
- **Data Layer:** `ApplicationDbContext` with EF Core 10, check constraints, filtered unique indexes, and `InitialCreate` migration.
- **Security:** ASP.NET Core Identity with PBKDF2 password hashing, 5-attempt lockout, anti-forgery verification on all mutating requests, and `ActiveMemberMiddleware`.

---

## 2. Admin & Member Modules

### A. Admin Console
1. **Command Center (Dashboard):** Live metrics, quick actions, recent circulation, and return activities.
2. **Book Collection:** Full CRUD, safe archival (retains history), restoration of archived titles, and ISBN-10/13 checksum validation.
3. **Category Management:** Taxonomy definitions with assigned title counts.
4. **Member Accounts:** Borrower registry, search, and one-click account suspension / reactivation.
5. **Issue Desk:** Issuance with borrowing quotas, duplicate active loan checks, and overdue debt holds.
6. **Return Desk:** Return processing with automatic overdue fine computation and inventory restoration.
7. **Overdue Register:** Dedicated register for overdue loans with days overdue and accrued fine tallies.
8. **Lost Book Settlement:** Workflow to mark unrecoverable books as Lost, stop daily fines, charge replacement penalties, and adjust inventory.
9. **Fine Management & Payment Ledger:** Full receipt history with cash/bank/card tracking and reference notes.
10. **Reports & Analytics:** 7-day activity visual charts, leaderboards, and 1-click CSV data exports (Books, Loans, Fines).
11. **Admin Profile:** Self-service contact management and credential updates.
12. **Library Settings:** Dynamic web-based configuration of loan duration, fine rates, grace periods, and borrowing limits.

### B. Member Portal
1. **Borrower Dashboard:** Active loans, due date countdowns, overdue alerts, and outstanding fee balance.
2. **Public Catalogue:** Search by keyword, author, or category with real-time stock availability badges.
3. **Book Details:** Bibliographic records and shelf status.
4. **Borrowing History:** Log of past and current loans with fine settlement statuses.
5. **Personal Profile:** Contact information updates and password change portal.

---

## 3. Automated Business Logic Verification

The project includes an integrated automated test suite covering:
- ISBN-10 checksum validation (including 'X' check digit)
- ISBN-13 checksum validation (including 0 check digit)
- Normalization (stripping prefixes, dashes, and whitespace)
- Overdue day calculation and edge cases
- Grace period exemptions
- Daily rate fine calculations
- Accrued fines on active loans
- Settled balances on returned and lost loans

Run the automated test suite anytime with:
```bash
dotnet run -- --run-tests
```

---

## 4. Arch Linux Setup & Deployment Instructions

### Prerequisites on Arch Linux
- **.NET 10 SDK and ASP.NET Core Targeting Pack:**
  ```bash
  sudo pacman -S dotnet-sdk aspnet-targeting-pack
  ```
- **SQL Server 2022 Instance:**  
  On Arch Linux, run SQL Server 2022 in Docker or connect to an external SQL Server instance:
  ```bash
  read -rsp "SQL Server SA password: " MSSQL_SA_PASSWORD
  printf '\n'
  export MSSQL_SA_PASSWORD
  sudo docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=$MSSQL_SA_PASSWORD" \
     -p 1433:1433 --name sqlserver -d mcr.microsoft.com/mssql/server:2022-latest
  ```

### Step-by-Step Execution

1. **Navigate to the Arch Linux project directory:**
   ```bash
   cd "/path/to/LibraryManagementSystem"
   ```

2. **Restore dependencies and local tools:**
   ```bash
   dotnet restore
   dotnet tool restore
   ```

3. **Verify automated business logic tests (requires no database):**
   ```bash
   dotnet run -- --run-tests
   ```

4. **Set SQL Server connection string environment variable:**
   ```bash
   export ConnectionStrings__DefaultConnection="Server=localhost,1433;Database=LibraryManagementSystem;User Id=sa;Password=$MSSQL_SA_PASSWORD;Encrypt=True;TrustServerCertificate=True"
   ```

5. **Apply Database Migrations & Provision Initial Administrator:**
   Pass the disposable administrator credentials via environment variables:
   ```bash
   export LIBRARY_ADMIN_EMAIL="admin@shelfwise.local"
   read -rsp "Initial admin password (used only if that account does not exist): " LIBRARY_ADMIN_PASSWORD
   printf '\n'
   export LIBRARY_ADMIN_PASSWORD

   dotnet run -- --initialize-database
   ```
   This will:
   - Apply EF Core migrations (`InitialCreate`).
   - Create Identity roles (`Admin`, `Member`).
   - Seed default categories and library configuration rules.
   - Provision the initial administrator account.
   `LIBRARY_ADMIN_PASSWORD` is required only when the account does not already exist. The initializer never resets an existing password, and sample account passwords are not embedded in source code.

   To check SQL Server connectivity and execute read-only dashboard, report, catalogue, member, circulation, and payment queries without applying migrations or changing data, run:
   ```bash
   dotnet run -- --run-sqlserver-smoke
   ```

6. **Start Web Application:**
   ```bash
   dotnet watch run
   ```
   Open <http://localhost:5180> in your web browser.

### Local Admin Recovery

Admin password recovery is available only as a local Development command against SQL Server. Set the new password without echoing it, explicitly authorize the target account, then run:

```bash
read -rsp "New Admin password: " SHELFWISE_ADMIN_RECOVERY_PASSWORD
printf '\n'
export SHELFWISE_ADMIN_RECOVERY_PASSWORD
export SHELFWISE_CONFIRM_ADMIN_RECOVERY="admin@shelfwise.local"
dotnet run -- --recover-admin-account
unset SHELFWISE_ADMIN_RECOVERY_PASSWORD SHELFWISE_CONFIRM_ADMIN_RECOVERY
```

The command verifies the password with ASP.NET Core Identity and confirms the account is active and unlocked. It does not expose an HTTP endpoint or log the password.
