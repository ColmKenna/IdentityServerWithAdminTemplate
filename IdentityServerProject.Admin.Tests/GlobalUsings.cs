global using IdentityServerProject.Services;
using IdentityServerProject.Configuration;
using Microsoft.Extensions.Options;

namespace IdentityServerProject.Admin.Tests.Infrastructure;

public static class TestOptions
{
    public const int PageSize = 10;

    public static IOptions<AdminConsoleOptions> AdminConsole { get; } =
        Options.Create(new AdminConsoleOptions
        {
            DefaultPageSize = PageSize
        });
}