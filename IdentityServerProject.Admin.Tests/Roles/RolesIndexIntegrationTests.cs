using System.Net;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using IdentityServerProject.Admin.Tests.Infrastructure;
using IdentityServerProject.Services.Roles;
using IdentityServerProject.Services.Validation;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace IdentityServerProject.Admin.Tests.Roles;

public class RolesIndexIntegrationTests : IDisposable
{
    private readonly List<IDisposable> _disposables = new();

    public void Dispose()
    {
        foreach (IDisposable disposable in _disposables) disposable.Dispose();
    }

    private HttpClient CreateClient(IRoleService roleService, bool allowAutoRedirect = true)
    {
        var baseFactory = new AdminWebFactory();
        _disposables.Add(baseFactory);

        WebApplicationFactory<Program> factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services => { services.AddSingleton(roleService); });
        });
        _disposables.Add(factory);

        return factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = allowAutoRedirect
        });
    }

    private static async Task<IDocument> GetDocumentAsync(HttpResponseMessage response)
    {
        string content = await response.Content.ReadAsStringAsync();
        IBrowsingContext context = BrowsingContext.New(AngleSharp.Configuration.Default);
        return await context.OpenAsync(req => req.Content(content));
    }

    private static async Task<(string Token, string Cookie)> ExtractAntiForgeryTokenAndCookieAsync(
        HttpClient httpClient, string pageUrl)
    {
        HttpResponseMessage response = await httpClient.GetAsync(pageUrl);
        IDocument document = await GetDocumentAsync(response);

        var tokenInput = document.QuerySelector("input[name='__RequestVerificationToken']") as IHtmlInputElement;
        Assert.NotNull(tokenInput);

        string token = tokenInput!.Value;

        IEnumerable<string> cookies = response.Headers.GetValues("Set-Cookie");
        string? cookie = cookies.FirstOrDefault(c => c.StartsWith(".AspNetCore.Antiforgery"));
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

        HttpClient client = CreateClient(mock.Object);
        HttpResponseMessage response = await client.GetAsync("/Admin/Roles");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        IDocument document = await GetDocumentAsync(response);

        IHtmlCollection<IElement> rows = document.QuerySelectorAll("ck-responsive-row");
        Assert.True(rows.Length >= 2);
    }

    [Fact]
    public async Task Get_RolesCreate_RendersForm()
    {
        var mock = new Mock<IRoleService>();
        HttpClient client = CreateClient(mock.Object);

        HttpResponseMessage response = await client.GetAsync("/Admin/Roles/Create");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        IDocument document = await GetDocumentAsync(response);

        Assert.NotNull(document.QuerySelector("input[name='Input.Name']"));
    }

    [Fact]
    public async Task Post_RolesCreate_ValidInput_RedirectsToIndex()
    {
        var mock = new Mock<IRoleService>();
        mock.Setup(s => s.CreateRoleAsync(It.IsAny<RoleCreateInputModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RoleCreateResult.Succeeded(RoleId.Create("role-new")));

        HttpClient client = CreateClient(mock.Object, false);
        (string token, string cookie) = await ExtractAntiForgeryTokenAndCookieAsync(client, "/Admin/Roles/Create");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Roles/Create");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Input.Name"] = "SupportEngineer"
        });

        HttpResponseMessage response = await client.SendAsync(request);

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

        HttpClient client = CreateClient(mock.Object, false);
        (string token, string cookie) = await ExtractAntiForgeryTokenAndCookieAsync(client, "/Admin/Roles");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Roles?id=role-custom&handler=Delete");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        });

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }
}