// Secrets required before `dotnet run` will succeed. Run these from the Sales.AppHost directory:
//
//   dotnet user-secrets set "Parameters:sql-password"            "<a strong SQL Server SA password>"
//   dotnet user-secrets set "Parameters:razor-client-secret"     "<a random secret string>"
//   dotnet user-secrets set "Parameters:blazor-client-secret"    "<a random secret string>"
//   dotnet user-secrets set "Parameters:seed-sysadmin-password"  "<a strong password>"
//   dotnet user-secrets set "Parameters:seed-test-user-password" "<a strong password>"

using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

var sqlPort = builder.Configuration.GetValue<int?>("SqlServer:Port");

if (string.IsNullOrEmpty(builder.Configuration["Parameters:sql-password"]))
{
    throw new InvalidOperationException(
        "Missing required secret 'Parameters:sql-password'. Set it with: " +
        "dotnet user-secrets set \"Parameters:sql-password\" \"<password>\" (run from Sales.AppHost).");
}

var sqlPassword = builder.AddParameter("sql-password", secret: true);
var razorClientSecret = builder.AddParameter("razor-client-secret", secret: true);
var blazorClientSecret = builder.AddParameter("blazor-client-secret", secret: true);
var sysAdminPassword = builder.AddParameter("seed-sysadmin-password", secret: true);
var testUserPassword = builder.AddParameter("seed-test-user-password", secret: true);

var sqlServer = builder.AddSqlServer("sqlserver", sqlPassword, port: sqlPort)
    .WithDataVolume();

var identityDb = sqlServer.AddDatabase("IdentityDb");
var identityConfigDb = sqlServer.AddDatabase("IdentityConfigDb");
var identityOperationalDb = sqlServer.AddDatabase("IdentityOperationalDb");
var salesDb = sqlServer.AddDatabase("SalesDb");

var wasmClient = builder.AddProject<Projects.Sales_WasmClient>("wasmclient")
    .WithExternalHttpEndpoints()
    .WithHttpsEndpoint(port: 5002, name: "https");

var identityServer = builder.AddProject<Projects.IdentityServerProject>("identityserver")
    .WithReference(identityDb)
    .WithReference(identityConfigDb)
    .WithReference(identityOperationalDb)
    .WaitFor(sqlServer)
    .WithHttpsEndpoint(port: 5001, name: "https")
    .WithEnvironment("Clients__RazorClientUri", "https://localhost:5001")
    .WithEnvironment("Clients__BlazorClientUri", wasmClient.GetEndpoint("https"))
    .WithEnvironment("Clients__RazorSecret", razorClientSecret)
    .WithEnvironment("Clients__BlazorSecret", blazorClientSecret)
    .WithEnvironment("Seed__SysAdminPassword", sysAdminPassword)
    .WithEnvironment("Seed__TestUserPassword", testUserPassword);

var apiService = builder.AddProject<Projects.Sales_ApiService>("apiservice")
    .WithReference(salesDb)
    .WithReference(identityServer)
    .WaitFor(sqlServer)
    .WaitFor(identityServer)
    .WithHttpsEndpoint(port: 5004, name: "https")
    .WithHttpHealthCheck("/health");

wasmClient
    .WithReference(identityServer)
    .WithReference(apiService)
    .WaitFor(identityServer)
    .WaitFor(apiService)
    .WithEnvironment("IdentityServer__ClientSecret", blazorClientSecret);

builder.Build().Run();
