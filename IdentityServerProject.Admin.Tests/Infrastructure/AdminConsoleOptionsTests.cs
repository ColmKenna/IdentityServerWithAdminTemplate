using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace IdentityServerProject.Admin.Tests.Infrastructure;

public class AdminConsoleOptionsTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("201")]
    public async Task InvalidDefaultPageSize_FailsHostStartup(string configuredPageSize)
    {
        using var baseFactory = new AdminWebFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AdminConsole:DefaultPageSize"] = configuredPageSize
                })));

        var exception = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });
            await client.GetAsync("/Admin");
        });

        Assert.Contains("DefaultPageSize", exception.ToString(), StringComparison.Ordinal);
    }
}
