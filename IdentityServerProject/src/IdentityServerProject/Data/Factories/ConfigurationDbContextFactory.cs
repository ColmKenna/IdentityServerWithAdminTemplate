using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace IdentityServerProject.Data.Factories;

public class ConfigurationDbContextFactory : IDesignTimeDbContextFactory<ConfigurationDbContext>
{
    public ConfigurationDbContext CreateDbContext(string[] args)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new ConfigurationStoreOptions());
        ServiceProvider serviceProvider = services.BuildServiceProvider();

        var optionsBuilder = new DbContextOptionsBuilder<ConfigurationDbContext>();
        optionsBuilder.UseApplicationServiceProvider(serviceProvider);
        optionsBuilder.UseSqlServer(
            "Server=(localdb)\\mssqllocaldb;Database=IdentityConfigDb_DesignTime;Trusted_Connection=True;MultipleActiveResultSets=true",
            sql => sql.MigrationsAssembly(typeof(ConfigurationDbContextFactory).Assembly.FullName));

        return new ConfigurationDbContext(optionsBuilder.Options);
    }
}