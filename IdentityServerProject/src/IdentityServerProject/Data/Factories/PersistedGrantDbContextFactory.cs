using Duende.IdentityServer.EntityFramework.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace IdentityServerProject.Data.Factories;

public class PersistedGrantDbContextFactory : IDesignTimeDbContextFactory<PersistedGrantDbContext>
{
    public PersistedGrantDbContext CreateDbContext(string[] args)
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton(new Duende.IdentityServer.EntityFramework.Options.OperationalStoreOptions());
        var serviceProvider = services.BuildServiceProvider();

        var optionsBuilder = new DbContextOptionsBuilder<PersistedGrantDbContext>();
        optionsBuilder.UseApplicationServiceProvider(serviceProvider);
        optionsBuilder.UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=IdentityOperationalDb_DesignTime;Trusted_Connection=True;MultipleActiveResultSets=true", 
            sql => sql.MigrationsAssembly(typeof(PersistedGrantDbContextFactory).Assembly.FullName));

        return new PersistedGrantDbContext(optionsBuilder.Options);
    }
}
