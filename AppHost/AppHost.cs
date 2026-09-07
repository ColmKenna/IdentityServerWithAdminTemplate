// Secrets required before `dotnet run` will succeed. Run these from the AppHost directory:
//
//   dotnet user-secrets set "Parameters:sql-password"            "<a strong SQL Server SA password>"
//   dotnet user-secrets set "Parameters:razor-client-secret"     "<a random secret string>"
//   dotnet user-secrets set "Parameters:blazor-client-secret"    "<a random secret string>"
//   dotnet user-secrets set "Parameters:seed-sysadmin-password"  "<a strong password>"
//   dotnet user-secrets set "Parameters:seed-test-user-password" "<a strong password>"

using Microsoft.Extensions.Configuration;
using Projects;

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

int? sqlPort = builder.Configuration.GetValue<int?>("SqlServer:Port");

if (string.IsNullOrEmpty(builder.Configuration["Parameters:sql-password"]))
    throw new InvalidOperationException(
        "Missing required secret 'Parameters:sql-password'. Set it with: " +
        "dotnet user-secrets set \"Parameters:sql-password\" \"<password>\" (run from AppHost).");

IResourceBuilder<ParameterResource> sqlPassword = builder.AddParameter("sql-password", true);
IResourceBuilder<ParameterResource> razorClientSecret = builder.AddParameter("razor-client-secret", true);
IResourceBuilder<ParameterResource> blazorClientSecret = builder.AddParameter("blazor-client-secret", true);
IResourceBuilder<ParameterResource> sysAdminPassword = builder.AddParameter("seed-sysadmin-password", true);
IResourceBuilder<ParameterResource> testUserPassword = builder.AddParameter("seed-test-user-password", true);

IResourceBuilder<SqlServerServerResource> sqlServer = builder.AddSqlServer("sqlserver", sqlPassword, sqlPort)
    .WithDataVolume();

IResourceBuilder<SqlServerDatabaseResource> identityDb = sqlServer.AddDatabase("IdentityDb");
IResourceBuilder<SqlServerDatabaseResource> identityConfigDb = sqlServer.AddDatabase("IdentityConfigDb");
IResourceBuilder<SqlServerDatabaseResource> identityOperationalDb = sqlServer.AddDatabase("IdentityOperationalDb");

IResourceBuilder<ProjectResource> identityServer = builder.AddProject<IdentityServerProject>("identityserver")
    .WithReference(identityDb)
    .WithReference(identityConfigDb)
    .WithReference(identityOperationalDb)
    .WaitFor(sqlServer)
    .WithHttpsEndpoint(5001, name: "https")
    .WithEnvironment("Clients__RazorClientUri", "https://localhost:5001")
    .WithEnvironment("Clients__BlazorClientUri", "https://localhost:5002")
    .WithEnvironment("Clients__RazorSecret", razorClientSecret)
    .WithEnvironment("Clients__BlazorSecret", blazorClientSecret)
    .WithEnvironment("Seed__SysAdminPassword", sysAdminPassword)
    .WithEnvironment("Seed__TestUserPassword", testUserPassword);

builder.Build().Run();