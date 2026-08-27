using System.Net;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using IdentityServerProject.Admin.Tests.Infrastructure;
using IdentityServerProject.Services.Users;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace IdentityServerProject.Admin.Tests.Users;

public class UsersCreateIntegrationTests : IDisposable
{
    private readonly List<IDisposable> _disposables = new();

    public void Dispose()
    {
        foreach (IDisposable disposable in _disposables) disposable.Dispose();
    }

    private HttpClient CreateClient(IUserCreateService userCreateService, bool allowAutoRedirect = true)
    {
        var baseFactory = new AdminWebFactory();
        _disposables.Add(baseFactory);

        WebApplicationFactory<Program> factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services => { services.AddSingleton(userCreateService); });
        });
        _disposables.Add(factory);

        return factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = allowAutoRedirect
        });
    }

    private static IUserCreateService MockService(UserCreateResult? result = null)
    {
        var mock = new Mock<IUserCreateService>();
        mock.Setup(s => s.CreateUserAsync(It.IsAny<UserCreateInputModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result ?? UserCreateResult.Succeeded("new-user-id"));
        return mock.Object;
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

        return (token, cookie!);
    }

    private static Dictionary<string, string> ValidFormValues(string token) => new()
    {
        { "__RequestVerificationToken", token },
        { "Input.UserName", "jane.doe" },
        { "Input.Email", "jane.doe@example.com" },
        { "Input.FullName", "Jane Doe" },
        { "Input.Password", "Password123!" },
        { "Input.ConfirmPassword", "Password123!" }
    };

    [Fact]
    public async Task Get_ReturnsForm()
    {
        HttpClient httpClient = CreateClient(MockService());

        HttpResponseMessage response = await httpClient.GetAsync("/Admin/Users/Create");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        IDocument document = await GetDocumentAsync(response);
        Assert.NotNull(document.QuerySelector("input[name='Input.UserName']"));
        Assert.NotNull(document.QuerySelector("input[name='Input.Email']"));
        Assert.NotNull(document.QuerySelector("input[name='Input.Password']"));
        Assert.NotNull(document.QuerySelector("input[name='Input.ConfirmPassword']"));
    }

    [Fact]
    public async Task Post_ValidSubmission_RedirectsToIndex()
    {
        var mock = new Mock<IUserCreateService>();
        mock.Setup(s => s.CreateUserAsync(It.IsAny<UserCreateInputModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(UserCreateResult.Succeeded("new-user-id"));

        HttpClient httpClient = CreateClient(mock.Object, false);
        (string token, string cookie) = await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Users/Create");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Users/Create")
        {
            Content = new FormUrlEncodedContent(ValidFormValues(token))
        };
        request.Headers.Add("Cookie", cookie);

        HttpResponseMessage response = await httpClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Admin/Users", response.Headers.Location?.OriginalString);

        mock.Verify(s => s.CreateUserAsync(
            It.Is<UserCreateInputModel>(i => i.UserName == "jane.doe" && i.Email == "jane.doe@example.com"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Post_DuplicateUserName_RedisplaysFormWithError()
    {
        HttpClient httpClient = CreateClient(
            MockService(UserCreateResult.Failed(new List<string> { "Username 'jane.doe' is already taken." })),
            false);
        (string token, string cookie) = await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Users/Create");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Users/Create")
        {
            Content = new FormUrlEncodedContent(ValidFormValues(token))
        };
        request.Headers.Add("Cookie", cookie);

        HttpResponseMessage response = await httpClient.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        IDocument document = await GetDocumentAsync(response);
        IElement? summary = document.QuerySelector(".validation-summary");
        Assert.NotNull(summary);
        Assert.Contains("already taken", summary!.TextContent);
    }

    [Fact]
    public async Task Post_DuplicateEmail_RedisplaysFormWithError()
    {
        HttpClient httpClient = CreateClient(
            MockService(UserCreateResult.Failed(new List<string> { "Email 'jane.doe@example.com' is already taken." })),
            false);
        (string token, string cookie) = await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Users/Create");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Users/Create")
        {
            Content = new FormUrlEncodedContent(ValidFormValues(token))
        };
        request.Headers.Add("Cookie", cookie);

        HttpResponseMessage response = await httpClient.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        IDocument document = await GetDocumentAsync(response);
        IElement? summary = document.QuerySelector(".validation-summary");
        Assert.NotNull(summary);
        Assert.Contains("Email", summary!.TextContent);
    }

    [Fact]
    public async Task Post_PasswordMismatch_RedisplaysFormWithValidationErrorWithoutCallingService()
    {
        var mock = new Mock<IUserCreateService>();
        HttpClient httpClient = CreateClient(mock.Object, false);
        (string token, string cookie) = await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Users/Create");

        Dictionary<string, string> formValues = ValidFormValues(token);
        formValues["Input.ConfirmPassword"] = "SomethingElse123!";

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Users/Create")
        {
            Content = new FormUrlEncodedContent(formValues)
        };
        request.Headers.Add("Cookie", cookie);

        HttpResponseMessage response = await httpClient.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        IDocument document = await GetDocumentAsync(response);
        IElement? summary = document.QuerySelector(".validation-summary");
        Assert.NotNull(summary);

        mock.Verify(s => s.CreateUserAsync(It.IsAny<UserCreateInputModel>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Post_WeakPasswordRejectedByService_RedisplaysFormWithError()
    {
        HttpClient httpClient = CreateClient(
            MockService(UserCreateResult.Failed(new List<string>
                { "Passwords must have at least one non alphanumeric character." })),
            false);
        (string token, string cookie) = await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Users/Create");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Users/Create")
        {
            Content = new FormUrlEncodedContent(ValidFormValues(token))
        };
        request.Headers.Add("Cookie", cookie);

        HttpResponseMessage response = await httpClient.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        IDocument document = await GetDocumentAsync(response);
        Assert.NotNull(document.QuerySelector(".validation-summary"));
    }
}