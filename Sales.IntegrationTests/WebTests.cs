using Aspire.Hosting.Testing;
using Projects;

namespace Sales.Tests;

public class WebTests
{
    [Fact]
    public async Task AppHostDefinesTheCurrentDistributedApplicationResources()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string[] args = new[]
        {
            "--Parameters:sql-password=Integration_Only_Password!1",
            "--Parameters:razor-client-secret=integration-razor-secret",
            "--Parameters:blazor-client-secret=integration-blazor-secret",
            "--Parameters:seed-sysadmin-password=Integration_Admin_Password!1",
            "--Parameters:seed-test-user-password=Integration_User_Password!1"
        };

        IDistributedApplicationTestingBuilder appHost =
            await DistributedApplicationTestingBuilder.CreateAsync<Sales_AppHost>(
                args,
                cancellationToken);
        var resourceNames = appHost.Resources.Select(resource => resource.Name).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("sqlserver", resourceNames);
        Assert.Contains("IdentityDb", resourceNames);
        Assert.Contains("IdentityConfigDb", resourceNames);
        Assert.Contains("IdentityOperationalDb", resourceNames);
        Assert.Contains("SalesDb", resourceNames);
        Assert.Contains("identityserver", resourceNames);
        Assert.Contains("apiservice", resourceNames);
        Assert.Contains("wasmclient", resourceNames);
        Assert.DoesNotContain("webfrontend", resourceNames);
    }
}