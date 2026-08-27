using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Options;
using IdentityServerProject;
using IdentityServerProject.Data;
using IdentityServerProject.Services.Validation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Sales.Tests;

public class SeedDataTests
{
    private const string RazorUri = "https://localhost:5002";
    private const string BlazorUri = "https://localhost:5003";
    private const string RazorSecret = "razor-secret";
    private const string BlazorSecret = "blazor-secret";
    private const string SysAdminPassword = "SysAdmin!Passw0rd";
    private const string SysAdminEmail = "admin@sales.local";
    private const string TestUserPassword = "TestUser!Passw0rd";

    private static ServiceProvider BuildProvider(string dbName)
    {
        var services = new ServiceCollection();

        services.AddSingleton(new ConfigurationStoreOptions());
        services.AddSingleton(new OperationalStoreOptions());

        services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase($"{dbName}-identity"));
        services.AddDbContext<ConfigurationDbContext>(o => o.UseInMemoryDatabase($"{dbName}-config"),
            optionsLifetime: ServiceLifetime.Singleton);
        services.AddDbContext<PersistedGrantDbContext>(o => o.UseInMemoryDatabase($"{dbName}-operational"),
            optionsLifetime: ServiceLifetime.Singleton);

        services.AddLogging();
        services
            .AddIdentityCore<ApplicationUser>(options => options.Password.RequiredLength = 8)
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>();

        return services.BuildServiceProvider();
    }

    private static async Task SeedOnceAsync(ServiceProvider provider)
    {
        using IServiceScope scope = provider.CreateScope();
        IServiceProvider services = scope.ServiceProvider;

        var clients = new List<SeedClientSpec>
        {
            new("razorclient", "Sales Razor Client", AbsoluteHttpUri.Create(RazorUri), RazorSecret),
            new("blazorclient", "Sales Blazor Client", AbsoluteHttpUri.Create(BlazorUri), BlazorSecret)
        };

        await SeedData.SeedAsync(
            services.GetRequiredService<ApplicationDbContext>(),
            services.GetRequiredService<ConfigurationDbContext>(),
            services.GetRequiredService<PersistedGrantDbContext>(),
            services.GetRequiredService<UserManager<ApplicationUser>>(),
            services.GetRequiredService<RoleManager<IdentityRole>>(),
            clients,
            SysAdminEmail, SysAdminPassword, TestUserPassword);
    }

    [Fact]
    public async Task SeedAsync_CreatesExpectedClientsRolesAndUsers()
    {
        using ServiceProvider provider = BuildProvider(nameof(SeedAsync_CreatesExpectedClientsRolesAndUsers));

        await SeedOnceAsync(provider);

        using IServiceScope scope = provider.CreateScope();
        ConfigurationDbContext configDb = scope.ServiceProvider.GetRequiredService<ConfigurationDbContext>();
        UserManager<ApplicationUser> userManager =
            scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        RoleManager<IdentityRole> roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        Assert.Equal(2, configDb.Clients.Count());
        Assert.Contains(configDb.Clients, c => c.ClientId == "razorclient");
        Assert.Contains(configDb.Clients, c => c.ClientId == "blazorclient");

        Assert.True(await roleManager.RoleExistsAsync(Config.SysAdminRole));

        ApplicationUser? admin = await userManager.FindByNameAsync("admin@sales.local");
        Assert.NotNull(admin);
        Assert.True(await userManager.IsInRoleAsync(admin!, Config.SysAdminRole));

        ApplicationUser? testUser = await userManager.FindByNameAsync("testuser@sales.local");
        Assert.NotNull(testUser);
        Assert.False(await userManager.IsInRoleAsync(testUser!, Config.SysAdminRole));
    }

    [Fact]
    public async Task SeedAsync_IsIdempotent_WhenRunTwice()
    {
        using ServiceProvider provider = BuildProvider(nameof(SeedAsync_IsIdempotent_WhenRunTwice));

        await SeedOnceAsync(provider);
        await SeedOnceAsync(provider);

        using IServiceScope scope = provider.CreateScope();
        ConfigurationDbContext configDb = scope.ServiceProvider.GetRequiredService<ConfigurationDbContext>();
        UserManager<ApplicationUser> userManager =
            scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        Assert.Equal(2, configDb.Clients.Count());
        Assert.Equal(4, configDb.IdentityResources.Count());
        Assert.Single(configDb.ApiScopes);
        Assert.Single(configDb.ApiResources);

        int admins = userManager.Users.Count(u => u.UserName == "admin@sales.local");
        Assert.Equal(1, admins);
    }

    [Fact]
    public async Task SeedIfDevelopmentAsync_RunsSeed_OnlyInDevelopmentEnvironment()
    {
        bool ranInDevelopment = false;
        await DevelopmentSeeder.SeedIfDevelopmentAsync(new FakeHostEnvironment(Environments.Development), () =>
        {
            ranInDevelopment = true;
            return Task.CompletedTask;
        });
        Assert.True(ranInDevelopment);

        bool ranInProduction = false;
        await DevelopmentSeeder.SeedIfDevelopmentAsync(new FakeHostEnvironment(Environments.Production), () =>
        {
            ranInProduction = true;
            return Task.CompletedTask;
        });
        Assert.False(ranInProduction);
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public FakeHostEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
        }

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "Sales.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}