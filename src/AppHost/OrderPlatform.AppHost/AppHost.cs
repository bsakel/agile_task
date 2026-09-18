var builder = DistributedApplication.CreateBuilder(args);

// Same image major version, database name and development password as docker-compose (ADR-0013).
// The password is a fixed parameter, not Aspire's generated one: PostgreSQL only applies POSTGRES_PASSWORD when it
// initialises an empty data directory, so as soon as the generated password changes it no longer matches the data
// volume below. The container then runs but never becomes healthy, and the Migrator and Api wait for it forever.
var postgresPassword = builder.AddParameter("postgres-password", "orderplatform-dev", secret: true);
var postgres = builder.AddPostgres("postgres", password: postgresPassword)
    .WithImageTag("17-alpine")
    .WithDataVolume("orderplatform-postgres");

var database = postgres.AddDatabase("orderplatform");

// Keycloak as a plain container: Aspire.Hosting.Keycloak is preview-only (spike S5, ADR-0013).
// Fixed host port so token issuer URLs are stable across runs.
var keycloakAdminPassword = builder.AddParameter("keycloak-admin-password", "admin", secret: true);
var keycloak = builder.AddContainer("keycloak", "quay.io/keycloak/keycloak", "26.7.4")
    .WithArgs("start-dev", "--import-realm")
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", "admin")
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", keycloakAdminPassword)
    .WithEnvironment("KC_HEALTH_ENABLED", "true")
    .WithBindMount(Path.Combine(builder.AppHostDirectory, "..", "..", "..", "deploy", "keycloak"), "/opt/keycloak/data/import", isReadOnly: true)
    .WithHttpEndpoint(port: 8080, targetPort: 8080, name: "http")
    .WithHttpEndpoint(targetPort: 9000, name: "management")
    .WithHttpHealthCheck("/health/ready", endpointName: "management");

// The Migrator runs to completion before the Api starts, exactly as in deployment (ADR-0008).
var migrator = builder.AddProject<Projects.OrderPlatform_Migrator>("migrator")
    .WithReference(database)
    .WaitFor(database);

builder.AddProject<Projects.OrderPlatform_Api>("api")
    .WithReference(database)
    .WaitFor(database)
    .WaitForCompletion(migrator)
    .WaitFor(keycloak)
    .WithHttpHealthCheck("/health/ready");

builder.Build().Run();
