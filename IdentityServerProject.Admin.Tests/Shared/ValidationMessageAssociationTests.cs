using System.Net;
using AngleSharp;
using AngleSharp.Dom;
using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Entities;
using IdentityServerProject.Admin.Tests.Infrastructure;
using IdentityServerProject.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityServerProject.Admin.Tests.Shared;

/// <summary>
///     Every field-level validation message carries a stable id and its control points at it with
///     aria-describedby, so an assistive technology can report the error against the field it
///     belongs to. These check the rendered markup, because the association only exists there.
/// </summary>
public class ValidationMessageAssociationTests : IDisposable
{
    private readonly List<IDisposable> _disposables = new();

    public void Dispose()
    {
        foreach (IDisposable disposable in _disposables) disposable.Dispose();
    }

    private AdminWebFactory CreateFactory()
    {
        var factory = new AdminWebFactory();
        _disposables.Add(factory);
        return factory;
    }

    private static async Task<IDocument> GetDocumentAsync(HttpResponseMessage response)
    {
        string content = await response.Content.ReadAsStringAsync();
        IBrowsingContext context = BrowsingContext.New(AngleSharp.Configuration.Default);
        return await context.OpenAsync(request => request.Content(content));
    }

    /// <summary>
    ///     Asserts that ids are unique, that every aria-describedby token resolves to a real
    ///     element, and that the expected number of controls carry a validation association.
    /// </summary>
    private static void AssertAssociationsResolve(IDocument document, string url, int expectedAssociations)
    {
        string[] duplicateIds = document.QuerySelectorAll("[id]")
            .Select(element => element.Id!)
            .GroupBy(id => id)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        Assert.True(duplicateIds.Length == 0, $"{url}: duplicate ids {string.Join(", ", duplicateIds)}");

        IElement[] described = document.QuerySelectorAll("[aria-describedby]").ToArray();

        foreach (IElement control in described)
        {
            Assert.Contains(control.TagName, new[] { "INPUT", "TEXTAREA", "SELECT" });

            foreach (string token in control.GetAttribute("aria-describedby")!
                         .Split(' ', StringSplitOptions.RemoveEmptyEntries))
                Assert.True(document.GetElementById(token) is not null,
                    $"{url}: aria-describedby='{token}' on <{control.TagName.ToLowerInvariant()} " +
                    $"name='{control.GetAttribute("name")}'> resolves to nothing");
        }

        // Confirmation modals describe their confirm input with a "-desc" body id and are not
        // field validation, so only the "-error" references are counted here.
        int associations = described.Count(element =>
            element.GetAttribute("aria-describedby")!
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Any(token => token.EndsWith("-error", StringComparison.Ordinal)));

        Assert.True(expectedAssociations == associations,
            $"{url}: expected {expectedAssociations} validation associations, found {associations}: " +
            string.Join(" | ", described.Select(element => element.GetAttribute("aria-describedby"))));
    }

    public static TheoryData<string, int> FormPages() => new()
    {
        { "/Account/Login", 2 },
        { "/Admin/ApiScopes/Create", 3 },
        { "/Admin/IdentityResources/Create", 3 },
        { "/Admin/Roles/Create", 1 },
        { "/Admin/Users/Create", 5 },
        // Three repeatable URI groups, each rendering one empty row.
        { "/Admin/Clients/Create", 3 }
    };

    [Theory]
    [MemberData(nameof(FormPages))]
    public async Task FormPage_PointsEveryControlAtItsValidationMessage(string url, int expectedAssociations)
    {
        HttpResponseMessage response = await CreateFactory().CreateClient().GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        AssertAssociationsResolve(await GetDocumentAsync(response), url, expectedAssociations);
    }

    [Fact]
    public async Task EditorPages_PointEveryControlAtItsValidationMessage()
    {
        AdminWebFactory factory = CreateFactory();
        HttpClient client = factory.CreateClient();

        await factory.RunInScopeAsync(async serviceProvider =>
        {
            ConfigurationDbContext configurationDb =
                serviceProvider.GetRequiredService<ConfigurationDbContext>();
            configurationDb.ApiScopes.Add(new ApiScope { Name = "assoc.scope", DisplayName = "Assoc", Enabled = true });
            configurationDb.IdentityResources.Add(new IdentityResource
            {
                Name = "assoc.resource", DisplayName = "Assoc", Enabled = true
            });
            configurationDb.ApiResources.Add(new ApiResource
            {
                Name = "assoc.api", DisplayName = "Assoc", Enabled = true
            });
            configurationDb.Clients.Add(new Client
            {
                ClientId = "assoc.client", ClientName = "Assoc", Enabled = true
            });
            await configurationDb.SaveChangesAsync();
        });

        string userId = string.Empty;
        await factory.RunInScopeAsync(async serviceProvider =>
        {
            var users = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = "assoc.user", Email = "assoc@example.test" };

            IdentityResult created = await users.CreateAsync(user, "Password123!");
            Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(error => error.Description)));

            userId = user.Id;
        });

        (string Url, int Expected)[] pages =
        {
            ("/Admin/ApiScopes/Edit?name=assoc.scope", 2),
            ("/Admin/IdentityResources/Edit?name=assoc.resource", 2),
            ("/Admin/Apis/Editor?name=assoc.api", 6),
            // The attach-scope form loads its options only on the Scopes tab.
            ("/Admin/Apis/Editor?name=assoc.api&tab=scopes", 7),
            ("/Admin/Clients/Clone/assoc.client", 3),
            ($"/Admin/Users/ResetPassword/{userId}", 2)
        };

        foreach ((string url, int expected) in pages)
        {
            HttpResponseMessage response = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            AssertAssociationsResolve(await GetDocumentAsync(response), url, expected);
        }
    }

    [Fact]
    public async Task InvalidPost_RedisplaysTheErrorInsideTheReferencedElement()
    {
        HttpClient client = CreateFactory().CreateClient();

        HttpResponseMessage getResponse = await client.GetAsync("/Admin/Roles/Create");
        IDocument getDocument = await GetDocumentAsync(getResponse);

        // A valid form already renders the referenced element, so the reference is never dangling.
        IElement? emptyMessage = getDocument.GetElementById("role-name-error");
        Assert.NotNull(emptyMessage);
        Assert.Equal(string.Empty, emptyMessage.TextContent.Trim());

        string token = getDocument.QuerySelector("input[name='__RequestVerificationToken']")!
            .GetAttribute("value")!;
        string cookie = getResponse.Headers.GetValues("Set-Cookie")
            .First(value => value.StartsWith(".AspNetCore.Antiforgery"))
            .Split(';')[0];

        var post = new HttpRequestMessage(HttpMethod.Post, "/Admin/Roles/Create")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Input.Name"] = string.Empty,
                ["__RequestVerificationToken"] = token
            })
        };
        post.Headers.Add("Cookie", cookie);

        HttpResponseMessage postResponse = await client.SendAsync(post);
        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);

        IDocument postDocument = await GetDocumentAsync(postResponse);
        AssertAssociationsResolve(postDocument, "/Admin/Roles/Create (invalid POST)", 1);

        IElement? redisplayedMessage = postDocument.GetElementById("role-name-error");
        Assert.NotNull(redisplayedMessage);
        Assert.False(string.IsNullOrWhiteSpace(redisplayedMessage.TextContent),
            "the redisplayed error text must land inside the referenced element");

        Assert.Equal("role-name-error",
            postDocument.QuerySelector("input[name='Input.Name']")!.GetAttribute("aria-describedby"));
    }
}
