// WithHttpsCertificate is still marked experimental in Aspire 13.6.
#pragma warning disable ASPIRECERTIFICATES001

var builder = DistributedApplication.CreateBuilder(args);

// Keycloak and the API present a localhost certificate from the SmartRail development CA, so a
// test device that trusts that CA can reach both over HTTPS. See services/README.md.
var httpsCertificate = DevCertificates.LoadOrCreateServerCertificate();

// One PostgreSQL server with two databases. The named volume keeps data across restarts.
var postgres = builder.AddPostgres("postgres")
    .WithDataVolume("smartrail-postgres-data");
var smartrailDb = postgres.AddDatabase("smartrail");
var keycloakDb = postgres.AddDatabase("keycloak-db", "keycloak");

// Keycloak on a fixed port with a fixed hostname, so the token issuer is always
// https://localhost:8080/realms/smartrail. Its data lives in the keycloak database.
var keycloak = builder.AddKeycloak("keycloak", 8080)
    .WithRealmImport("./realms")
    .WithHttpsCertificate(httpsCertificate)
    .WithEnvironment("KC_HOSTNAME", "https://localhost:8080")
    .WithEnvironment("KC_DB", "postgres")
    .WithEnvironment("KC_DB_URL", ReferenceExpression.Create(
        $"jdbc:postgresql://{postgres.Resource.Host}:{postgres.Resource.Port}/{keycloakDb.Resource.DatabaseName}"))
    .WithEnvironment("KC_DB_USERNAME", postgres.Resource.UserNameReference)
    .WithEnvironment("KC_DB_PASSWORD", postgres.Resource.PasswordParameter)
    .WaitFor(keycloakDb);

// Secrets for the food-ordering boundary. Aspire generates each one on first run and keeps it in the
// AppHost's user secrets, so nothing is written to Git. They are shown, hidden by default, in the
// dashboard's parameters list: the drill in services/README.md reads them from there.
IResourceBuilder<ParameterResource> Secret(string name) =>
    builder.AddParameter(name, new GenerateParameterDefault { MinLength = 32, Special = false }, secret: true, persist: true);

var railbitesServiceKey = Secret("railbites-service-key");      // SmartRail -> RailBites
var railbitesAdminKey = Secret("railbites-admin-key");          // RailBites' own policy changes (the cutoff)
var smartrailInboundKey = Secret("smartrail-inbound-key");      // RailBites -> SmartRail passage read
var callbackSecret = Secret("railbites-callback-secret");       // HMAC secret for status callbacks
var kioskKeyPolgahawela = Secret("kiosk-key-polgahawela");
var kioskKeyPeradeniya = Secret("kiosk-key-peradeniya");

var api = builder.AddProject<Projects.SmartRail_Api>("api")
    .WithHttpsCertificate(httpsCertificate)
    .WithReference(smartrailDb)
    .WaitFor(smartrailDb)
    .WithHttpHealthCheck("/health/ready")
    .WithEnvironment("RailBites__ServiceKey", railbitesServiceKey)
    .WithEnvironment("RailBites__CallbackSecret", callbackSecret)
    .WithEnvironment("RailBites__InboundServiceKey", smartrailInboundKey);

// RailBites keeps its own SQLite file outside the build output, so orders and stock survive a
// restart of the AppHost. Delete services/railbites/data to start fresh.
var railbitesDatabase = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "railbites", "data", "railbites.db"));

var railbites = builder.AddJavaScriptApp("railbites", "../railbites", "dev")
    .WithEnvironment("RAILBITES_DB_PATH", railbitesDatabase)
    .WithEnvironment("RAILBITES_SERVICE_KEY", railbitesServiceKey)
    .WithEnvironment("RAILBITES_ADMIN_KEY", railbitesAdminKey)
    .WithEnvironment("RAILBITES_CALLBACK_SECRET", callbackSecret)
    .WithEnvironment("SMARTRAIL_SERVICE_KEY", smartrailInboundKey)
    .WithEnvironment("RAILBITES_KIOSK_KEYS", ReferenceExpression.Create(
        $"polgahawela-junction={kioskKeyPolgahawela},peradeniya-junction={kioskKeyPeradeniya}"))
    // Local drills only: the 503 outage switch. Never set outside this local stack.
    .WithEnvironment("RAILBITES_DEV_CONTROLS", "1")
    .WithHttpEndpoint(env: "PORT")
    // Ready, not just alive: the dashboard shows RailBites unhealthy during a simulated outage.
    .WithHttpHealthCheck("/health/ready");

// Each service finds the other by the address Aspire gives it. Neither waits for the other: the API
// serves train information with RailBites down, and RailBites queues what it cannot send yet.
api.WithEnvironment("RailBites__BaseUrl", railbites.GetEndpoint("http"));
railbites.WithEnvironment("SMARTRAIL_BASE_URL", api.GetEndpoint("http"));

builder.Build().Run();
