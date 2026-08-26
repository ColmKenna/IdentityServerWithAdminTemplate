using IdentityServerProject.Admin.Tests.Infrastructure;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Dom;
using IdentityServerProject.Services.Diagnostics;
using IdentityServerProject.Services.Users;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace IdentityServerProject.Admin.Tests.Diagnostics;

public class DiagnosticsIndexIntegrationTests : IDisposable
{
    private readonly List<IDisposable> _disposables = new();

    private HttpClient CreateClient(IDiagnosticsService diagnosticsService)
    {
        var baseFactory = new AdminWebFactory();
        _disposables.Add(baseFactory);

        var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(diagnosticsService);
            });
        });
        _disposables.Add(factory);

        return factory.CreateClient();
    }

    private static IDiagnosticsService MockService(DiagnosticsModel model)
    {
        var mock = new Mock<IDiagnosticsService>();
        mock.Setup(s => s.GetDiagnosticsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(model);
        return mock.Object;
    }

    private static async Task<IDocument> GetDocumentAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        var context = BrowsingContext.New(AngleSharp.Configuration.Default);
        return await context.OpenAsync(req => req.Content(content));
    }

    private static DiagnosticsModel AllHealthyModel() => new()
    {
        StoreHealth = new List<StoreHealthStatus>
        {
            new() { Name = "Identity Store", IsHealthy = true, Detail = "Connected" },
            new() { Name = "Configuration Store", IsHealthy = true, Detail = "Connected" },
            new() { Name = "Operational Store", IsHealthy = true, Detail = "Connected" }
        },
        SigningKeyId = "test-key-id-123",
        SigningAlgorithm = "RS256",
        ActiveValidationKeys = new List<SigningKeySummary>
        {
            new() { KeyId = "test-key-id-123", Algorithm = "RS256", IsX509Certificate = false }
        }
    };

    [Fact]
    public async Task Get_RendersHealthyBadgesForAllStores()
    {
        var httpClient = CreateClient(MockService(AllHealthyModel()));

        var response = await httpClient.GetAsync("/Admin/Diagnostics");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var document = await GetDocumentAsync(response);
        var badges = document.QuerySelectorAll(".status-badge");
        Assert.Equal(3, badges.Length);
        Assert.All(badges, b => Assert.Equal("Healthy", b.TextContent.Trim()));
    }

    [Fact]
    public async Task Get_DegradedStore_RendersDegradedBadge()
    {
        var model = AllHealthyModel();
        model.StoreHealth[1] = new StoreHealthStatus { Name = "Configuration Store", IsHealthy = false, Detail = "Unable to connect to the store." };

        var httpClient = CreateClient(MockService(model));

        var response = await httpClient.GetAsync("/Admin/Diagnostics");
        var document = await GetDocumentAsync(response);

        var configCard = document.QuerySelector("#store-health-configuration-store");
        Assert.NotNull(configCard);
        var badge = configCard!.QuerySelector(".status-badge");
        Assert.NotNull(badge);
        Assert.Equal("Degraded", badge!.TextContent.Trim());
        Assert.Contains("Unable to connect", configCard.TextContent);
    }

    [Fact]
    public async Task Get_RendersSigningKeyIdAndAlgorithm()
    {
        var httpClient = CreateClient(MockService(AllHealthyModel()));

        var response = await httpClient.GetAsync("/Admin/Diagnostics");
        var document = await GetDocumentAsync(response);

        var keyIdElement = document.QuerySelector("#active-signing-key-id");
        Assert.NotNull(keyIdElement);
        Assert.Equal("test-key-id-123", keyIdElement!.TextContent.Trim());
        Assert.Contains("RS256", document.Body!.TextContent);
    }

    [Fact]
    public async Task Get_RendersActiveValidationKeysList()
    {
        var httpClient = CreateClient(MockService(AllHealthyModel()));

        var response = await httpClient.GetAsync("/Admin/Diagnostics");
        var document = await GetDocumentAsync(response);

        var keysList = document.QuerySelector("#active-validation-keys");
        Assert.NotNull(keysList);
        Assert.Contains("test-key-id-123", keysList!.TextContent);
    }

    [Fact]
    public async Task Get_NoReservedClaims_RendersEmptyState()
    {
        var httpClient = CreateClient(MockService(AllHealthyModel()));

        var response = await httpClient.GetAsync("/Admin/Diagnostics");
        var document = await GetDocumentAsync(response);

        Assert.NotNull(document.QuerySelector("#reserved-claims-empty"));
        Assert.Null(document.QuerySelector("#reserved-claim-holders"));
    }

    [Fact]
    public async Task Get_ReservedClaimHolders_RendersEachWithALinkToTheirClaimsTab()
    {
        var model = AllHealthyModel();
        model.ReservedClaimHolders = new List<ReservedClaimHolder>
        {
            new()
            {
                UserId = UserId.Create("user-42"),
                UserName = "jane.doe",
                ClaimType = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role",
                ClaimValue = "SysAdmin"
            }
        };

        var httpClient = CreateClient(MockService(model));

        var response = await httpClient.GetAsync("/Admin/Diagnostics");
        var document = await GetDocumentAsync(response);

        Assert.Null(document.QuerySelector("#reserved-claims-empty"));

        var list = document.QuerySelector("#reserved-claim-holders");
        Assert.NotNull(list);
        Assert.Contains("jane.doe", list!.TextContent);
        Assert.Contains("SysAdmin", list.TextContent);

        var link = list.QuerySelector("a");
        Assert.NotNull(link);
        Assert.Contains("user-42", link!.GetAttribute("href"));
        Assert.Contains("tab=claims", link.GetAttribute("href"));
    }

    [Fact]
    public async Task Get_RendersInsideAdminLayoutShell()
    {
        var httpClient = CreateClient(MockService(AllHealthyModel()));

        var response = await httpClient.GetAsync("/Admin/Diagnostics");
        var document = await GetDocumentAsync(response);

        Assert.NotNull(document.QuerySelector("aside.sidebar"));
    }

    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }
    }
}
