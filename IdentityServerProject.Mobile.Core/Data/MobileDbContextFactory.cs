using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace IdentityServerProject.Mobile.Core.Data;

/// <summary>
///     Design-time only: lets <c>dotnet ef migrations add</c> build the context without the app's
///     dependency-injection container. See docs/mobile/README.md for the exact command.
/// </summary>
internal sealed class MobileDbContextFactory : IDesignTimeDbContextFactory<MobileDbContext>
{
    public MobileDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<MobileDbContext>()
            .UseSqlite("Data Source=design-time.db3")
            .Options);
}
