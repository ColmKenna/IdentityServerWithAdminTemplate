using IdentityServerProject.Admin.Tests.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Dom;
using IdentityServerProject.Services.Users;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace IdentityServerProject.Admin.Tests.Users;

public class UsersCreateIntegrationTests : IDisposable
{
    private readonly List<IDisposable> _disposables = new();

    private HttpClient CreateClient(IUserCreateService userCreateService, bool allowAutoRedirect = true)
    {
        var baseFactory = new AdminWebFactory();
        _disposables.Add(baseFactory);

        var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(userCreateService);
            });
        });
        _disposables.Add(factory);

        return factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
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
        var httpClient = CreateClient(MockService());

        var response = await httpClient.GetAsync("/Admin/Users/Create");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var document = await GetDocumentAsync(response);
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

        var httpClient = CreateClient(mock.Object, allowAutoRedirect: false);
        var (token, cookie) = await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Users/Create");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Users/Create")
        {
            Content = new FormUrlEncodedContent(ValidFormValues(token))
        };
        request.Headers.Add("Cookie", cookie);

        var response = await httpClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Admin/Users", response.Headers.Location?.OriginalString);

        mock.Verify(s => s.CreateUserAsync(
            It.Is<UserCreateInputModel>(i => i.UserName == "jane.doe" && i.Email == "jane.doe@example.com"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Post_DuplicateUserName_RedisplaysFormWithError()
    {
        var httpClient = CreateClient(
            MockService(UserCreateResult.Failed(new List<string> { "Username 'jane.doe' is already taken." })),
            allowAutoRedirect: false);
        var (token, cookie) = await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Users/Create");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Users/Create")
        {
            Content = new FormUrlEncodedContent(ValidFormValues(token))
        };
        request.Headers.Add("Cookie", cookie);

        var response = await httpClient.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var document = await GetDocumentAsync(response);
        var summary = document.QuerySelector(".validation-summary");
        Assert.NotNull(summary);
        Assert.Contains("already taken", summary!.TextContent);
    }

    [Fact]
    public async Task Post_DuplicateEmail_RedisplaysFormWithError()
    {
        var httpClient = CreateClient(
            MockService(UserCreateResult.Failed(new List<string> { "Email 'jane.doe@example.com' is already taken." })),
            allowAutoRedirect: false);
        var (token, cookie) = await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Users/Create");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Users/Create")
        {
            Content = new FormUrlEncodedContent(ValidFormValues(token))
        };
        request.Headers.Add("Cookie", cookie);

        var response = await httpClient.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var document = await GetDocumentAsync(response);
        var summary = document.QuerySelector(".validation-summary");
        Assert.NotNull(summary);
        Assert.Contains("Email", summary!.TextContent);
    }

    [Fact]
    public async Task Post_PasswordMismatch_RedisplaysFormWithValidationErrorWithoutCallingService()
    {
        var mock = new Mock<IUserCreateService>();
        var httpClient = CreateClient(mock.Object, allowAutoRedirect: false);
        var (token, cookie) = await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Users/Create");

        var formValues = ValidFormValues(token);
        formValues["Input.ConfirmPassword"] = "SomethingElse123!";

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Users/Create")
        {
            Content = new FormUrlEncodedContent(formValues)
        };
        request.Headers.Add("Cookie", cookie);

        var response = await httpClient.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var document = await GetDocumentAsync(response);
        var summary = document.QuerySelector(".validation-summary");
        Assert.NotNull(summary);

        mock.Verify(s => s.CreateUserAsync(It.IsAny<UserCreateInputModel>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Post_WeakPasswordRejectedByService_RedisplaysFormWithError()
    {
        var httpClient = CreateClient(
            MockService(UserCreateResult.Failed(new List<string> { "Passwords must have at least one non alphanumeric character." })),
            allowAutoRedirect: false);
        var (token, cookie) = await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Users/Create");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Users/Create")
        {
            Content = new FormUrlEncodedContent(ValidFormValues(token))
        };
        request.Headers.Add("Cookie", cookie);

        var response = await httpClient.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var document = await GetDocumentAsync(response);
        Assert.NotNull(document.QuerySelector(".validation-summary"));
    }

    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }
    }
}
