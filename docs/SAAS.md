# SpazaSnap SaaS architecture and deployment

SpazaSnap is implemented as a shared, centrally deployable application serving multiple independently authenticated shops. Each owner creates an account through the browser and receives a workspace. The operator deploys and updates the application; shops do not install or maintain a database server.

## SaaS capabilities in this code

| Capability | Implementation |
| --- | --- |
| Browser-delivered service | ASP.NET Core serves the responsive app and API. |
| Self-service onboarding | Register a shop with an email and password. One owner account per shop in this MVP. |
| Authentication | ASP.NET Core encrypted HttpOnly, SameSite Strict cookie; 8-hour sliding expiry. Secure cookies in Production. PasswordHasher creates salted password hashes. |
| Tenant isolation | TenantContext reads the shop ID from the encrypted session. Business queries include that ID; request bodies cannot override it. Composite foreign keys prevent cross-shop product/photo/order relationships. |
| Persistent service data | Shared MySQL schema with shop-scoped records; photos and cookie keys on persistent storage. |
| Plans | Actual server-side Free-plan limits: 100 products and 1,000 photos per shop. No fictional paid subscription or payment status. |
| Basic abuse controls | Account rate limits by client IP, 15 requests per minute; five failed passwords lock the account for 15 minutes. CSRF protection uses an obligatory custom header and same-origin checks, with no cross-origin API permission. |
| Central operation | Container deployment, HTTPS proxy configuration, health endpoint, test workflows and deployment build artifact. |
| Offline access | App-shell caching, shop-scoped browser snapshots and drafts. APIs and photos are never cached by the service worker. Sign-out clears that shop's browser data. |

SaaS describes how software is delivered and operated. A paid subscription is a business choice, not a prerequisite. To describe this as a **live SaaS service**, an operator still needs to deploy it and run it for users.

## Single-node HTTPS deployment

Use a server with Docker Engine and Docker Compose v2, reachable on ports 80 and 443. Point a real domain's DNS records at that server. This deployment uses a fixed private Docker subnet `172.30.50.0/24`; change both the subnet and `Deployment__TrustedProxy` if it conflicts with the host network.

1. Copy the source project to the server. Copy `.env.example` to `.env` and set long, unique database passwords plus `DOMAIN=your-real-domain`. Do not commit `.env`.
2. Run `docker compose -f compose.saas.yaml up --build -d` from the project root.
3. Inspect `docker compose -f compose.saas.yaml ps` and logs. The initializer must exit successfully; the database must be healthy; the app and HTTPS proxy must stay running.
4. Open `https://your-real-domain`. Caddy obtains an HTTPS certificate using the configured domain. Create two distinct shops and check that they see separate catalogues. Check `https://your-real-domain/api/health` for readiness.

The app and database have no public host ports in this deployment; only Caddy exposes 80/443. ASP.NET Core trusts forwarded protocol/client-IP headers only from the configured Caddy address, not arbitrary callers. Secure cookies require the public HTTPS connection. Local development uses the Development environment so HTTP on localhost can work.

Named volumes preserve the database, photos, Data Protection keys and Caddy certificates. The app container runs as its built-in unprivileged `app` user. Initializing a brand-new volume creates tables; optional catalogue samples are created for each shop only when requested during registration. This is the initial multi-tenant schema, not a migration from an older single-shop prototype. Future schema changes need explicit, versioned migrations and a backup before deployment.

## Operations before admitting real customers

- Establish automated, encrypted MySQL backups and photo-volume backups; verify restoration, not only backup creation. Protect and back up the Data Protection key directory. Keys are persisted, but the default file provider does not itself encrypt them at rest; use encrypted storage with restrictive permissions or integrate a managed key-protection service.
- Set uptime monitoring against `/api/health`, log retention, alert routing, storage quotas and a patch/update policy. Per-process account rate limits are suitable for this single-node MVP; use a shared limiter when adding nodes.
- Add an email provider for verified email addresses and password recovery. This MVP provides neither email verification nor a self-service reset flow. Complete account deletion/data export, retention rules, privacy notices and support processes before opening general customer registration.
- Connect a real vision provider for automatic shelf counts and review its error handling with local products. Connect a provisioned USSD gateway and supplier order confirmation before claiming live offline ordering. Define supplier contracts, prices and fulfilment status separately from drafts.
- Add a payment provider with verified, signed, idempotent webhooks if introducing paid tiers. Until then, all shops use the Free plan; no card collection or subscription billing occurs.
- Review the supported isiZulu and Sesotho labels with fluent speakers. The interface does not yet translate all product data or detailed forms.
- Arrange a security review, dependency maintenance and load testing. Current tests are useful checks, not evidence of a complete security or availability guarantee.

## Scaling boundary

The included configuration runs one web-app instance. MySQL is central, but uploaded files and cookie keys are on a volume attached to that instance. Before horizontal scaling, use shared/object storage for photos, shared and protected cookie keys, a distributed rate limiter, production schema migrations and centralized logging. Do not claim autoscaling, high availability, real-time ML forecasting or guaranteed offline order placement from this MVP.

## Data and account model

`shops` own `users`, `products`, `photos`, `orders` and stock adjustments. Each order item belongs to both a shop-owned order and a shop-owned product. Each photo-linked count belongs to both a shop-owned product and a shop-owned photo. Accounts are owner-only; staff roles, invitations and multi-shop account switching are future features.

The browser's offline snapshot is convenience storage, not encrypted customer storage. It is namespaced by shop and cleared on sign-out; users on shared devices should sign out while connected. Offline access after a previous sign-in relies on that browser tab's saved account context and snapshot and cannot validate an expired server session until it reconnects.
