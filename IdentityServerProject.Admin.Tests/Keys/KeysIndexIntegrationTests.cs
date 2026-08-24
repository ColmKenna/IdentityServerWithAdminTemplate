using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Dom;
using Duende.IdentityServer.Models;
using Duende.IdentityServer.Services;
using IdentityServerProject.Admin.Tests.Infrastructure;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Xunit;

namespace IdentityServerProject.Admin.Tests.Keys;

public class KeysIndexIntegrationTests : IDisposable
{
    private readonly List<IDisposable> _disposables = new();

    private HttpClient CreateClient(IKeyMaterialService keyMaterialService)
    {
        var baseFactory = new AdminWebFactory();
        _disposables.Add(baseFactory);

        var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(keyMaterialService);
            });
        });
        _disposables.Add(factory);

        return factory.CreateClient();
    }

    private static async Task<IDocument> GetDocumentAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        var context = BrowsingContext.New(AngleSharp.Configuration.Default);
        return await context.OpenAsync(req => req.Content(content));
    }

    [Fact]
    public async Task Get_KeysIndex_RendersKeysTable()
    {
        using var rsa = RSA.Create(2048);
        var rsaKey = new RsaSecurityKey(rsa.ExportParameters(true)) { KeyId = "test-rsa-key-1" };

        var mock = new Mock<IKeyMaterialService>();
        mock.Setup(s => s.GetValidationKeysAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SecurityKeyInfo>
            {
                new()
                {
                    Key = rsaKey,
                    SigningAlgorithm = "RS256"
                }
            });

        var client = CreateClient(mock.Object);
        var response = await client.GetAsync("/Admin/Keys");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await GetDocumentAsync(response);

        var table = document.QuerySelector("ck-responsive-table");
        Assert.NotNull(table);
        Assert.Contains("test-rsa-key-1", document.Body?.TextContent);
        Assert.Contains("RS256", document.Body?.TextContent);
    }

    [Fact]
    public async Task Get_KeysIndex_EmptyKeys_RendersEmptyState()
    {
        var mock = new Mock<IKeyMaterialService>();
        mock.Setup(s => s.GetValidationKeysAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SecurityKeyInfo>());

        var client = CreateClient(mock.Object);
        var response = await client.GetAsync("/Admin/Keys");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await GetDocumentAsync(response);

        var emptyState = document.QuerySelector(".empty-state");
        Assert.NotNull(emptyState);
        Assert.Contains("No keys found", emptyState!.TextContent);
    }

    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }
    }
}
