# Validation record

Verified on 31 October 2026 using .NET SDK 10.0.403, a local MySQL 8.4.13 Docker test database, and the Codex in-app browser. No production customer database was used.

| Check | Result |
| --- | --- |
| Release solution build | Passed, zero warnings and zero errors. |
| xUnit tests | 23 passed, zero failures/skips. TRX results and Cobertura coverage generated locally. |
| SaaS API/MySQL integration checks | 47 passed against live MySQL. |
| Schema initialization | Successfully run twice against the new multi-tenant database. |
| JavaScript syntax | `app.js` and `sw.js` passed `node --check`. |
| PowerShell syntax | Startup and integration scripts parsed without errors. |
| Startup script | `Start-SpazaSnap.ps1 -UseExistingMySql` successfully initialized the database and launched a ready app. The Docker-bootstrap branch was configuration-checked, not executed. |
| Workflow YAML | Both workflows and Dependabot settings parsed successfully; workflow triggers, jobs and explicit permissions checked. |
| Local and SaaS Compose configurations | Both accepted by `docker compose config --quiet`. |
| Docker build | Official .NET 10 multi-stage build completed successfully. |
| Linux container runtime | Readiness endpoint, registration, authenticated MySQL access and six shop-scoped sample products passed. Process confirmed running as user `app`, UID 1654. |
| Publish output | Application assembly, runtime settings, schema/seed files and web assets generated successfully. |
| Browser checks | Shop registration with samples, authenticated dashboard, account/plan details, inventory creation/search in the initial app, isiZulu navigation, desktop and 390px mobile layouts. |
| Service-unavailable browser check | App shell and saved shop snapshot loaded with server stopped; a USSD draft stayed on the device; after restart it synced to MySQL and remained a draft. |

The integration checks include anonymous-access rejection, email/password validation, duplicate signup rejection, catalogue persistence, invalid counts, restock suggestions, transactional order rollback, duplicate receipt protection, photo upload/retrieval, fake-photo rejection, CSRF checks, cross-shop product/order/photo isolation, foreign-photo link rejection, Free-plan quota enforcement, login lockout and sign-out.

The test script reports 47 assertions, including repeated failure checks for the lockout sequence; this is separate from the 23 xUnit test cases. It deliberately leaves test records in the isolated database.

## Verification boundaries

- GitHub Actions files are included and locally reviewed. They have **not** run on GitHub: no destination repository was supplied or modified.
- The container was built and exercised locally. A public domain, public HTTPS certificate issuance, hosting uptime and disaster recovery have not been verified.
- The included Compose recipe targets one app instance. Horizontal scaling and availability guarantees have not been tested.
- Automatic image counting, a live USSD gateway, supplier transmission, paid billing, email verification and password recovery are not connected.
- Partial local-language labels were checked for display, not certified for linguistic accuracy.
- A security review, load test and complete account lifecycle remain deployment work described in `docs/SAAS.md`.

The deliverable is a tested **SaaS MVP source project**, not a claim that a public production service is already operating.
