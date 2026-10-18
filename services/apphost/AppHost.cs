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

builder.AddProject<Projects.SmartRail_Api>("api")
    .WithHttpsCertificate(httpsCertificate)
    .WithReference(smartrailDb)
    .WaitFor(smartrailDb)
    .WithHttpHealthCheck("/health/ready");

// RailBites keeps its own SQLite file outside the build output, so orders and stock survive a
// restart of the AppHost. Delete services/railbites/data to start fresh.
var railbitesDatabase = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "railbites", "data", "railbites.db"));

builder.AddJavaScriptApp("railbites", "../railbites", "dev")
    .WithEnvironment("RAILBITES_DB_PATH", railbitesDatabase)
    .WithHttpEndpoint(env: "PORT")
    .WithHttpHealthCheck("/health/live");

builder.Build().Run();
