# SpazaSnap

[![version](https://img.shields.io/badge/version-v1.0.0-blue)](SpazaSnap.csproj)
[![Application checks](https://github.com/LethaboMohapi/SpazaSnap/actions/workflows/ci.yml/badge.svg?branch=master&event=push)](https://github.com/LethaboMohapi/SpazaSnap/actions/workflows/ci.yml)
[![license](https://img.shields.io/badge/license-MIT-blue)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](SpazaSnap.csproj)
[![MySQL](https://img.shields.io/badge/MySQL-8.4%2B-4479A1?logo=mysql&logoColor=white)](compose.saas.yaml)
![Code by Lethabo Mohapi](https://img.shields.io/badge/Code_by-Lethabo_Mohapi-blue)

Photo-first inventory for township spaza shops, built with **C#, ASP.NET Core 10 and MySQL 8.4**. A mobile-friendly, multi-tenant SaaS MVP: each shop owner signs in to a separate workspace for products, shelf photos, restock suggestions and supplier order drafts.

Copyright © 2026 Lethabo Mohapi. Licensed under the [MIT License](LICENSE).

## What works

- Self-service shop registration, sign-in, sign-out and encrypted session cookies.
- Separate inventory, photos, stock history and orders for every shop. Shop identity comes from the authenticated session; a caller cannot choose another shop by sending a shop ID.
- Optional sample products when creating a shop. New shops otherwise start empty.
- Create products, search inventory, confirm stock counts and record stock adjustments in MySQL.
- Upload JPEG/PNG shelf photos up to 6 MB and link a manually verified count to the photo.
- Estimate days of stock and suggest restocks using daily sales and reorder levels.
- Save supplier order drafts, download a draft, and receive delivered quantities into stock. Receiving is transactional and prevents double receipt.
- Cache the app shell and shop snapshot after an online visit. Save order drafts on this browser while offline, then explicitly sync them to MySQL.
- Free plan with a server-enforced limit of 100 products and 1,000 photos per shop.
- English interface, with isiZulu and Sesotho navigation and overview labels. Detailed forms remain in English; translations need fluent-speaker review.

**Feature boundaries:** photos require manual count confirmation; there is no connected computer-vision model. Restocks use a transparent formula, not a trained forecast. USSD and online orders are drafts; no supplier orders are transmitted. A real USSD service needs an aggregator/mobile-network agreement and provisioned short code. No paid billing gateway is configured. SaaS can be free; payment collection is separate from the service architecture.

## Run locally on Windows

Install the .NET 10 SDK, Docker Desktop with Linux containers, and PowerShell 7. Open the project folder in a PowerShell 7 terminal:

```powershell
./Start-SpazaSnap.ps1
```

The script creates private local `.env` settings if missing, starts MySQL on **127.0.0.1:3307**, initializes the tables, and starts the C# app at **http://localhost:5038**. Choose **Create your shop**. A password must have 12–128 characters, including a letter and a number. Sample products are optional. No shared demo login or real credentials are bundled.

Stop the web app with Ctrl+C. Stop the local database with `docker compose stop`. Database records remain in the Docker volume. Keep `.env` private; it is excluded from Git and the source package.

### Use an existing MySQL server

Create a dedicated database and user with permission on that database. For example, run as a MySQL administrator, replacing the password:

```sql
CREATE DATABASE spazasnap CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
CREATE USER 'spazasnap'@'localhost' IDENTIFIED BY 'REPLACE_WITH_YOUR_PASSWORD';
GRANT ALL PRIVILEGES ON spazasnap.* TO 'spazasnap'@'localhost';
```

Then, from PowerShell 7:

```powershell
$env:ConnectionStrings__MySql = 'Server=127.0.0.1;Port=3306;Database=spazasnap;User ID=spazasnap;Password=REPLACE_WITH_YOUR_PASSWORD'
./Start-SpazaSnap.ps1 -UseExistingMySql
```

Use the host grant appropriate to your actual MySQL network setup. The initializer creates tables in an existing database; it does not create a database or server user. Do not use a production root account as the application login.

### Open in Visual Studio

Open **SpazaSnap.sln** in a Visual Studio version that supports .NET 10. Set `SpazaSnap` as the startup project. Supply `ConnectionStrings__MySql` through your environment, initialize the database with `dotnet run -- --init-db`, and run the `http` launch profile. Test Explorer discovers the xUnit project. The stable SDK selection is defined in `global.json`.

## Tests and GitHub Actions

```powershell
dotnet restore SpazaSnap.sln
dotnet build SpazaSnap.sln --configuration Release --no-restore
dotnet test tests/SpazaSnap.Tests/SpazaSnap.Tests.csproj --configuration Release --no-build
node --check wwwroot/app.js
node --check wwwroot/sw.js
```

Node is needed only for the optional JavaScript syntax checks. The C# app does not require Node at runtime.

For integration tests, start the application against a **dedicated test database**, then run:

```powershell
./tests/Integration.ps1 -BaseUrl http://localhost:5038
```

Integration tests create labelled accounts and records, fill one shop's 100-product allowance, and deliberately lock its test account for 15 minutes. Use a fresh test database. Repeated runs within one minute may encounter the account rate limiter; this is expected. Never point these tests at customer data.

The unit suite covers restock boundaries, zero sales, fractional targets, email validation and password rules. Integration tests cover authentication, optional seeding, persistence, input validation, rollback, photo handling, CSRF checks, order receipt, tenant isolation, plan quotas and login lockout.

Included automation:

| File | Purpose |
| --- | --- |
| `.github/workflows/ci.yml` | On pushes to main/master, pull requests, or manual dispatch: restore, build, unit tests with coverage, JavaScript checks, MySQL service integration tests, then publish artifact and verify Docker build. |
| `.github/workflows/dependency-review.yml` | Review newly introduced dependencies for high-severity issues on public-repository pull requests. Skips private repositories where that feature may be unavailable. |
| `.github/dependabot.yml` | Weekly NuGet and GitHub Actions update pull requests. |

CI database passwords are disposable test values, not deployment credentials. Workflow and test-results artifacts are retained by GitHub Actions. The repository must be uploaded to GitHub for hosted runs; local test success does not prove a hosted CI run passed. No unsupported CI badge is included.

## SaaS deployment

See [the SaaS architecture and deployment guide](docs/SAAS.md). `Dockerfile` packages the C# app. `compose.saas.yaml` adds MySQL, persistent photo/key storage, database initialization and a Caddy HTTPS reverse proxy for your real domain.

This repository is a **SaaS MVP implementation**, with tenant isolation and an HTTPS hosting recipe. It is not a currently operated public service, and local verification is not a production security audit.

## Layout

```text
SpazaSnap.sln                    Visual Studio solution
Program.cs                      API routes, sessions, authorization, request protection
AccountStore.cs / Tenancy.cs     Shop onboarding, password hashing, authenticated shop context
StockStore.cs / Models.cs        MySQL operations, transactions, forecast rules
database/schema.sql             Multi-tenant schema and composite foreign keys
database/seed.sql               Optional samples inserted inside shop registration
wwwroot/                        Responsive UI and offline shell worker
tests/SpazaSnap.Tests/           xUnit unit tests
tests/Integration.ps1            API and MySQL integration tests
.github/                        CI, dependency review and Dependabot
Dockerfile / compose.saas.yaml   Hosted-service deployment
```

## License

[MIT](LICENSE). The license applies to this project. Third-party dependencies retain their own licenses. Example product names are illustrative catalogue data; no supplier affiliation is implied.

Implementation references: [MySqlConnector installation and connection guide](https://mysqlconnector.net/tutorials/connect-to-mysql/), [ASP.NET Core cookie authentication](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/cookie?view=aspnetcore-10.0), and [persistent Data Protection keys](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0).
