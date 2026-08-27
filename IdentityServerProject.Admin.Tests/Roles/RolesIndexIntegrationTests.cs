using System.Net;
using AngleSharp;
using AngleSharp.Dom;
using IdentityServerProject.Admin.Tests.Infrastructure;
using IdentityServerProject.Services.Roles;
using IdentityServerProject.Services.Validation;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace IdentityServerProject.Admin.Tests.Roles;

public class RolesIndexIntegrationTests : IDisposable
{
    private readonly List<IDisposable> _disposables = new();

    private HttpClient CreateClient(IRoleService roleService, bool allowAutoRedirect = true)
    {
        var baseFactory = new AdminWebFactory();
        _disposables.Add(baseFactory);

        var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(roleService);
            });
        });
        _disposables.Add(factory);

        return factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = allowAutoRedirect
        });
    }

    private static async Task<IDocument> GetDocumentAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        var context = BrowsingContext.New(AngleSharp.Configuration.Default);
        return await context.OpenAsync(req => req.Content(content));
    }

    private static async Task<(string Token, string Cookie)> ExtractAntiForgeryTokenAndCookieAsync(HttpClient httpClient, string pageUrl)
    {
        var response = await httpClient.GetAsync(pageUrl);
        var document = await GetDocumentAsync(response);

        var tokenInput = document.QuerySelector("input[name='__RequestVerificationToken']") as AngleSharp.Html.Dom.IHtmlInputElement;
        Assert.NotNull(tokenInput);

        var token = tokenInput!.Value;

        var cookies = response.Headers.GetValues("Set-Cookie");
        var cookie = cookies.FirstOrDefault(c => c.StartsWith(".AspNetCore.Antiforgery"));
        Assert.NotNull(cookie);

        return (token, cookie);
    }

    [Fact]
    public async Task Get_RolesIndex_RendersRolesTable()
    {
        var mock = new Mock<IRoleService>();
        mock.Setup(s => s.GetRolesAsync(It.IsAny<ListQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListResult<RoleListItem>
            {
                Items = new List<RoleListItem>
                {
                    new() { Id = RoleId.Create("role-1"), Name = "SysAdmin", IsProtected = true },
                    new() { Id = RoleId.Create("role-2"), Name = "Operator", IsProtected = false }
                },
                TotalCount = 2,
                PageNumber = 1,
                PageSize = 10
            });

        var client = CreateClient(mock.Object);
        var response = await client.GetAsync("/Admin/Roles");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await GetDocumentAsync(response);

        var rows = document.QuerySelectorAll("ck-responsive-row");
        Assert.True(rows.Length >= 2);
    }

    [Fact]
    public async Task Get_RolesCreate_RendersForm()
    {
        var mock = new Mock<IRoleService>();
        var client = CreateClient(mock.Object);

        var response = await client.GetAsync("/Admin/Roles/Create");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await GetDocumentAsync(response);

        Assert.NotNull(document.QuerySelector("input[name='Input.Name']"));
    }

    [Fact]
    public async Task Post_RolesCreate_ValidInput_RedirectsToIndex()
    {
        var mock = new Mock<IRoleService>();
        mock.Setup(s => s.CreateRoleAsync(It.IsAny<RoleCreateInputModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RoleCreateResult.Succeeded(RoleId.Create("role-new")));

        var client = CreateClient(mock.Object, allowAutoRedirect: false);
        var (token, cookie) = await ExtractAntiForgeryTokenAndCookieAsync(client, "/Admin/Roles/Create");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Roles/Create");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Input.Name"] = "SupportEngineer"
        });

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Admin/Roles", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Post_RolesDelete_ValidRole_RedirectsWithStatus()
    {
        var mock = new Mock<IRoleService>();
        mock.Setup(s => s.GetRolesAsync(It.IsAny<ListQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListResult<RoleListItem>
            {
                Items = new List<RoleListItem>
                {
                    new() { Id = RoleId.Create("role-custom"), Name = "CustomRole", IsProtected = false }
                },
                TotalCount = 1,
                PageNumber = 1,
                PageSize = 10
            });
        mock.Setup(s => s.DeleteRoleAsync(RoleId.Create("role-custom"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdminMutationResult.Success());

        var client = CreateClient(mock.Object, allowAutoRedirect: false);
        var (token, cookie) = await ExtractAntiForgeryTokenAndCookieAsync(client, "/Admin/Roles");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Roles?id=role-custom&handler=Delete");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        });

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }
    }
}
