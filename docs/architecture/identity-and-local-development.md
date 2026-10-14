# Identity and local development — pilot decision

Status: **accepted 14 October 2026**. This revises D9, D11 and D12 in [the scope decision](../06-scope-decision.md). It records the intended design; the Keycloak and Aspire setup is not implemented yet.

## Identity boundary

Keycloak authenticates passengers and staff. It owns passwords, sign-in screens, sessions, refresh tokens and signing keys. SmartRail is a resource server: it validates a Keycloak access token's issuer, signature, audience and expiry, then resolves the `(issuer, subject)` pair to a local `identity.accounts` row. SmartRail never accepts an email address or a role claim alone as proof of account ownership. Public railway reads remain anonymous.

SmartRail owns **authorization**: seeded roles and permission codes, station-limited grants, driver journey assignments, account status and audit entries. Keep these in SmartRail because Keycloak's general roles do not encode the journey and station rules of this pilot. A disabled local account loses access even if its Keycloak token has not expired. The local SysAdmin can manage SmartRail grants; provisioning or disabling a Keycloak identity is a separate Keycloak administration action for the pilot. Do not imply that the SmartRail screen creates Keycloak users.

React and Flutter clients use OpenID Connect Authorization Code with PKCE against Keycloak. Register browser and mobile clients with exact redirect URIs; treat them as public clients with no embedded client secret. The API receives bearer access tokens. Keycloak controls access-token lifetime and refresh-token rotation; configure and test them in the realm, rather than issuing either token in SmartRail. Store mobile tokens in secure platform storage. RailBites webhooks continue to use their signed HMAC contract; they do not masquerade as passengers or staff.

Seed a small demo realm and demo identities reproducibly. Keep realm/client configuration in source control **without credentials or personal data**. Local secrets come from Aspire parameters or user-secrets; Azure secrets come from Container Apps. Pin a tested Keycloak image version when implementing the stack. Test guest access, invalid issuer/audience/expired tokens, unknown or disabled local accounts, station and journey scope, and one passenger trying to access another's order.

## Local stack and telemetry

Use a .NET Aspire AppHost to start the SmartRail API, RailBites, PostgreSQL and Keycloak together. The AppHost supplies local service addresses and an Aspire dashboard. It is a development and demo convenience, not another production application and not a requirement to deploy through `aspire deploy`. The passenger and staff apps connect to the local public endpoints. A stable local Keycloak URL matters because browser redirects and issuer validation must agree. Local realm import is only for development; production realm seeding needs an explicit deployment step.

Instrument the API and RailBites with OpenTelemetry. Send local traces, logs and metrics to the Aspire dashboard; send production telemetry to Azure Application Insights. Keep correlation across API-to-RailBites calls, avoid personal data and token contents in logs, and cap production ingestion/retention. The local dashboard is in-memory and does not replace production storage.

## Azure and budget check

The Azure pilot needs a reachable HTTPS Keycloak endpoint and persistent PostgreSQL data. Run Keycloak separately from the API and RailBites, with its **own database** on the PostgreSQL Flexible Server; never place Keycloak tables in SmartRail's schemas. Keep one Keycloak replica available for the live demo and configure its public hostname/proxy settings so token issuer URLs match what clients and the API see. Use production mode, not Keycloak's `start-dev`. RailBites' SQLite file needs a separate persistence decision and a restart test: a Container App's writable filesystem is ephemeral. Validate a persistent mount with SQLite's locking behavior, or move RailBites to its own PostgreSQL database before calling the Azure order flow durable. Check that the chosen PostgreSQL tier, Keycloak memory allocation, three running Container Apps, storage and telemetry retention fit the USD 200 credit before provisioning; the earlier two-service cost estimate no longer applies. Document the measured estimate and a local Aspire demo fallback.

The implementation sequence is: start local Aspire dependencies; prove browser/mobile PKCE login and token validation; add local account mapping and scope checks; then deploy the same contracts to Azure and repeat the five demo journeys. The backend's broader Clean Architecture versus feature-slice project layout remains a separate decision.

Sources: [Aspire Keycloak integration](https://learn.microsoft.com/en-us/dotnet/aspire/authentication/keycloak-integration), [standalone Aspire dashboard](https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/dashboard/standalone), [Keycloak containers and production mode](https://www.keycloak.org/server/containers), [Azure Container Apps OpenTelemetry](https://learn.microsoft.com/en-us/azure/container-apps/opentelemetry-agents), [Container Apps storage mounts](https://learn.microsoft.com/en-us/azure/container-apps/storage-mounts).
