global using IdentityServerProject.Services;

namespace IdentityServerProject.Admin.Tests.Infrastructure;

public static class TestOptions
{
    public const int PageSize = 10;

    public static Microsoft.Extensions.Options.IOptions<IdentityServerProject.Configuration.AdminConsoleOptions> AdminConsole { get; } =
        Microsoft.Extensions.Options.Options.Create(new IdentityServerProject.Configuration.AdminConsoleOptions
        {
            DefaultPageSize = PageSize,
        });
}
