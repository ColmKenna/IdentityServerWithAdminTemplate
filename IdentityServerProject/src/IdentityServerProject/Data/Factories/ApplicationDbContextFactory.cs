using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace IdentityServerProject.Data.Factories;

public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.UseSqlServer(
            "Server=(localdb)\\mssqllocaldb;Database=IdentityDb_DesignTime;Trusted_Connection=True;MultipleActiveResultSets=true",
            sql => sql.MigrationsAssembly(typeof(ApplicationDbContextFactory).Assembly.FullName));

        return new ApplicationDbContext(optionsBuilder.Options);
    }
}