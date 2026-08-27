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

public class UsersDetailsIntegrationTests : IDisposable
{
    private readonly List<IDisposable> _disposables = new();

    public void Dispose()
    {
        foreach (IDisposable disposable in _disposables) disposable.Dispose();
    }

    private HttpClient CreateClient(IUserDetailsService userDetailsService, bool allowAutoRedirect = true)
    {
        var baseFactory = new AdminWebFactory();
        _disposables.Add(baseFactory);

        WebApplicationFactory<Program> factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services => { services.AddSingleton(userDetailsService); });
        });
        _disposables.Add(factory);

        return factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = allowAutoRedirect
        });
    }

    private static UserDetailsModel SampleUser(string id = "test-user", bool isCurrentUser = false) => new()
    {
        Id = id,
        UserName = "jane.doe",
        Email = "jane.doe@example.com",
        FullName = "Jane Doe",
        IsLockedOut = false,
        AssignedRoles = new List<string> { "SysAdmin" },
        AllRoles = new List<string> { "SysAdmin", "Support" },
        Claims = new List<UserClaimSummary> { new() { Type = "dept", Value = "Engineering" } },
        PersistedGrantCount = 2,
        IsCurrentUser = isCurrentUser
    };

    private static IUserDetailsService MockService(
        UserDetailsModel? details = null,
        RoleChangeResult? roleResult = null,
        ClaimChangeResult? claimResult = null,
        UserAccessRevokeResult? accessResult = null)
    {
        var mock = new Mock<IUserDetailsService>();
        mock.Setup(s => s.GetUserDetailsAsync(It.IsAny<UserActionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserActionContext context, CancellationToken __) => context.Target.Value == "non-existent"
                ? null
                : details ?? SampleUser(context.Target.Value));
        mock.Setup(s => s.AddRoleAsync(It.IsAny<UserId>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(roleResult ?? RoleChangeResult.Succeeded());
        mock.Setup(s => s.RemoveRoleAsync(It.IsAny<UserId>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(roleResult ?? RoleChangeResult.Succeeded());
        mock.Setup(s => s.AddClaimAsync(It.IsAny<UserId>(), It.IsAny<UserClaim>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(claimResult ?? ClaimChangeResult.Succeeded());
        mock.Setup(s => s.RemoveClaimAsync(It.IsAny<UserId>(), It.IsAny<UserClaim>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(claimResult ?? ClaimChangeResult.Succeeded());
        mock.Setup(s => s.RevokeUserAccessAsync(It.IsAny<UserActionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(accessResult ?? UserAccessRevokeResult.Succeeded(2));
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

    [Fact]
    public async Task Get_RendersExactlyOneBreadcrumbNav()
    {
        // WI-04: Details.cshtml uses the standard ViewData["Breadcrumbs"] convention (like every
        // other admin page) rather than hard-coded markup, so _AdminLayout renders the breadcrumb
        // nav exactly once - locking that in as a regression guard.
        HttpClient httpClient = CreateClient(MockService());

        HttpResponseMessage response = await httpClient.GetAsync("/Admin/Users/Details?id=test-user");
        IDocument document = await GetDocumentAsync(response);

        IHtmlCollection<IElement> breadcrumbNavs = document.QuerySelectorAll("nav[aria-label='Breadcrumb']");
        Assert.Single(breadcrumbNavs);

        IElement? crumbs = document.QuerySelector("#crumbs");
        Assert.NotNull(crumbs);
        Assert.Contains("jane.doe", crumbs!.TextContent);
    }

    [Fact]
    public async Task Get_NoTab_DefaultsToOverview()
    {
        HttpClient httpClient = CreateClient(MockService());

        HttpResponseMessage response = await httpClient.GetAsync("/Admin/Users/Details?id=test-user");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        IDocument document = await GetDocumentAsync(response);
        Assert.NotNull(document.QuerySelector("#tab-panel-overview"));
        IElement? activeTab = document.QuerySelector("#user-details-tabs > ck-tab[active]");
        Assert.NotNull(activeTab);
        Assert.Equal("Overview", activeTab!.GetAttribute("label"));
    }

    [Fact]
    public async Task Get_TabRoles_RendersRolesPanel()
    {
        HttpClient httpClient = CreateClient(MockService());

        HttpResponseMessage response = await httpClient.GetAsync("/Admin/Users/Details?id=test-user&tab=roles");
        IDocument document = await GetDocumentAsync(response);

        Assert.NotNull(document.QuerySelector("#tab-panel-roles"));
        Assert.Contains("SysAdmin", document.QuerySelector("#tab-panel-roles")!.TextContent);
        Assert.Equal("Roles", document.QuerySelector("#user-details-tabs > ck-tab[active]")!.GetAttribute("label"));
    }

    [Fact]
    public async Task Get_TabClaims_RendersClaimsPanel()
    {
        HttpClient httpClient = CreateClient(MockService());

        HttpResponseMessage response = await httpClient.GetAsync("/Admin/Users/Details?id=test-user&tab=claims");
        IDocument document = await GetDocumentAsync(response);

        IElement? panel = document.QuerySelector("#tab-panel-claims");
        Assert.NotNull(panel);
        Assert.Contains("dept", panel!.TextContent);
        Assert.Equal("Claims", document.QuerySelector("#user-details-tabs > ck-tab[active]")!.GetAttribute("label"));

        // An ordinary claim carries no reserved marker.
        Assert.Empty(panel.QuerySelectorAll(".badge-reserved"));
        Assert.NotNull(panel.QuerySelector("#claim-type-help"));
    }

    [Fact]
    public async Task Get_TabClaims_ReservedClaim_RendersRemediationMarker()
    {
        UserDetailsModel user = SampleUser();
        user.Claims = new List<UserClaimSummary>
        {
            new() { Type = "dept", Value = "Engineering", IsReserved = false },
            new() { Type = "role", Value = "SysAdmin", IsReserved = true }
        };

        HttpClient httpClient = CreateClient(MockService(user));

        HttpResponseMessage response = await httpClient.GetAsync("/Admin/Users/Details?id=test-user&tab=claims");
        IDocument document = await GetDocumentAsync(response);

        IElement reservedRow =
            Assert.Single(document.QuerySelectorAll("#tab-panel-claims .claim-row[data-reserved='true']"));
        Assert.Contains("role", reservedRow.TextContent);
        Assert.NotNull(reservedRow.QuerySelector(".badge-reserved"));

        // The reserved row still offers Remove - that is the remediation path.
        Assert.NotNull(reservedRow.QuerySelector("button[data-action='remove-claim']"));

        IElement ordinaryRow =
            Assert.Single(document.QuerySelectorAll("#tab-panel-claims .claim-row[data-reserved='false']"));
        Assert.Null(ordinaryRow.QuerySelector(".badge-reserved"));
    }

    [Fact]
    public async Task PostAddClaim_RejectedByService_ShowsTheErrorOnTheClaimsTab()
    {
        HttpClient httpClient = CreateClient(MockService(
            claimResult: ClaimChangeResult.Failed("'role' is a reserved claim type and cannot be assigned here.")));

        (string token, string cookie) =
            await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Users/Details?id=test-user&tab=claims");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Users/Details?id=test-user&handler=AddClaim");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["claimType"] = "role",
            ["claimValue"] = "SysAdmin",
            ["__RequestVerificationToken"] = token
        });

        HttpResponseMessage response = await httpClient.SendAsync(request);
        IDocument document = await GetDocumentAsync(response);

        IElement? alert = document.QuerySelector(".alert-error");
        Assert.NotNull(alert);
        Assert.Contains("reserved claim type", alert!.TextContent);
    }

    [Fact]
    public async Task Get_TabAccess_RendersAccessAndGrantsPanel()
    {
        HttpClient httpClient = CreateClient(MockService());

        HttpResponseMessage response = await httpClient.GetAsync("/Admin/Users/Details?id=test-user&tab=access");
        IDocument document = await GetDocumentAsync(response);

        IElement? panel = document.QuerySelector("#tab-panel-access");
        Assert.NotNull(panel);
        Assert.Contains("2", panel!.TextContent);
        Assert.Equal("Access & grants",
            document.QuerySelector("#user-details-tabs > ck-tab[active]")!.GetAttribute("label"));
    }

    [Fact]
    public async Task Get_RendersCkTabsWithAllWorkspacePanelsAndGuardedActions()
    {
        HttpClient httpClient = CreateClient(MockService(SampleUser(isCurrentUser: true)));

        HttpResponseMessage response = await httpClient.GetAsync("/Admin/Users/Details?id=test-user&tab=roles");
        IDocument document = await GetDocumentAsync(response);

        Assert.Equal(new[] { "Overview", "Roles", "Claims", "Access & grants", "Danger Zone" },
            document.QuerySelectorAll("#user-details-tabs > ck-tab")
                .Select(tab => tab.GetAttribute("label")));

        Assert.NotNull(document.QuerySelector("form[action*='handler=RemoveRole']"));
        Assert.NotNull(document.QuerySelector("form[action*='handler=AddClaim']"));
        Assert.NotNull(document.QuerySelector("form[action*='handler=RevokeUserAccess'] button[disabled]"));
        Assert.NotNull(document.QuerySelector("form[action*='handler=Suspend']"));
        Assert.NotNull(document.QuerySelector("form[action*='handler=Delete']"));
    }

    [Fact]
    public async Task Get_NonExistentUser_ReturnsNotFound()
    {
        HttpClient httpClient = CreateClient(MockService());

        HttpResponseMessage response = await httpClient.GetAsync("/Admin/Users/Details?id=non-existent");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_ViewingSelf_RendersDisabledRevokeAccessButton()
    {
        HttpClient httpClient = CreateClient(MockService(SampleUser(isCurrentUser: true)));

        HttpResponseMessage response = await httpClient.GetAsync("/Admin/Users/Details?id=test-user&tab=access");
        IDocument document = await GetDocumentAsync(response);

        var revokeButton = document.QuerySelector("#tab-panel-access button[type='submit']") as IHtmlButtonElement;
        Assert.NotNull(revokeButton);
        Assert.True(revokeButton!.IsDisabled);
    }

    [Fact]
    public async Task Get_ViewingSelf_RendersDisabledSysAdminRemovalButton()
    {
        HttpClient httpClient = CreateClient(MockService(SampleUser(isCurrentUser: true)));

        HttpResponseMessage response = await httpClient.GetAsync("/Admin/Users/Details?id=test-user&tab=roles");
        IDocument document = await GetDocumentAsync(response);

        var removeButton = document.QuerySelector(
                "#tab-panel-roles button[data-action='remove-role'][data-role='SysAdmin']")
            as IHtmlButtonElement;
        Assert.NotNull(removeButton);
        Assert.True(removeButton!.IsDisabled);
        Assert.Contains("own SysAdmin", removeButton.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Get_ViewingOther_RendersEnabledRevokeAccessButton()
    {
        HttpClient httpClient = CreateClient(MockService(SampleUser(isCurrentUser: false)));

        HttpResponseMessage response = await httpClient.GetAsync("/Admin/Users/Details?id=test-user&tab=access");
        IDocument document = await GetDocumentAsync(response);

        var revokeButton = document.QuerySelector("#tab-panel-access button[type='submit']") as IHtmlButtonElement;
        Assert.NotNull(revokeButton);
        Assert.False(revokeButton!.IsDisabled);
    }

    [Fact]
    public async Task PostAddRole_Succeeds_RedirectsToRolesTab()
    {
        var mock = new Mock<IUserDetailsService>();
        mock.Setup(s => s.GetUserDetailsAsync(It.Is<UserActionContext>(c => c.Target == UserId.Create("test-user")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleUser());
        mock.Setup(s => s.AddRoleAsync(UserId.Create("test-user"), "Support", It.IsAny<CancellationToken>()))
            .ReturnsAsync(RoleChangeResult.Succeeded());

        HttpClient httpClient = CreateClient(mock.Object, false);
        (string token, string cookie) =
            await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Users/Details?id=test-user&tab=roles");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Users/Details?id=test-user&handler=AddRole");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["role"] = "Support"
        });

        HttpResponseMessage response = await httpClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("tab=roles", response.Headers.Location?.OriginalString);
        mock.Verify(s => s.AddRoleAsync(UserId.Create("test-user"), "Support", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PostRemoveRole_BlockedByLastAdminGuard_RedirectsWithErrorAndDoesNotRemove()
    {
        string blockReason =
            "'jane.doe' is the last user in the 'SysAdmin' role. Assign the role to another user before removing it here.";
        HttpClient httpClient = CreateClient(
            MockService(roleResult: RoleChangeResult.Failed(blockReason)));
        (string token, string cookie) =
            await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Users/Details?id=test-user&tab=roles");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Users/Details?id=test-user&handler=RemoveRole");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["role"] = "SysAdmin"
        });

        HttpResponseMessage response = await httpClient.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // followed redirect

        IDocument document = await GetDocumentAsync(response);
        IElement? errorAlert = document.QuerySelector(".alert-error");
        Assert.NotNull(errorAlert);
        Assert.Contains("last user", errorAlert!.TextContent);
    }

    [Fact]
    public async Task PostAddClaim_Succeeds_RedirectsToClaimsTab()
    {
        var mock = new Mock<IUserDetailsService>();
        mock.Setup(s => s.GetUserDetailsAsync(It.Is<UserActionContext>(c => c.Target == UserId.Create("test-user")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleUser());
        mock.Setup(s => s.AddClaimAsync(UserId.Create("test-user"), new UserClaim("team", "platform"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ClaimChangeResult.Succeeded());

        HttpClient httpClient = CreateClient(mock.Object, false);
        (string token, string cookie) =
            await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Users/Details?id=test-user&tab=claims");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Users/Details?id=test-user&handler=AddClaim");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["claimType"] = "team",
            ["claimValue"] = "platform"
        });

        HttpResponseMessage response = await httpClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("tab=claims", response.Headers.Location?.OriginalString);
        mock.Verify(
            s => s.AddClaimAsync(UserId.Create("test-user"), new UserClaim("team", "platform"),
                It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PostRemoveClaim_Succeeds_RedirectsToClaimsTab()
    {
        var mock = new Mock<IUserDetailsService>();
        mock.Setup(s => s.GetUserDetailsAsync(It.Is<UserActionContext>(c => c.Target == UserId.Create("test-user")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleUser());
        mock.Setup(s => s.RemoveClaimAsync(UserId.Create("test-user"), new UserClaim("dept", "Engineering"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ClaimChangeResult.Succeeded());

        HttpClient httpClient = CreateClient(mock.Object, false);
        (string token, string cookie) =
            await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Users/Details?id=test-user&tab=claims");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Users/Details?id=test-user&handler=RemoveClaim");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["claimType"] = "dept",
            ["claimValue"] = "Engineering"
        });

        HttpResponseMessage response = await httpClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        mock.Verify(
            s => s.RemoveClaimAsync(UserId.Create("test-user"), new UserClaim("dept", "Engineering"),
                It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PostRevokeUserAccess_Allowed_RedirectsWithSuccessMessage()
    {
        HttpClient httpClient = CreateClient(
            MockService(accessResult: UserAccessRevokeResult.Succeeded(3)));
        (string token, string cookie) =
            await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Users/Details?id=test-user&tab=access");

        var request =
            new HttpRequestMessage(HttpMethod.Post, "/Admin/Users/Details?id=test-user&handler=RevokeUserAccess");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        });

        HttpResponseMessage response = await httpClient.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        IDocument document = await GetDocumentAsync(response);
        IElement? successAlert = document.QuerySelector(".alert-success");
        Assert.NotNull(successAlert);
        Assert.Contains("3", successAlert!.TextContent);
    }

    [Fact]
    public async Task PostRevokeUserAccess_NotificationFailure_RendersSuccessAndWarning()
    {
        HttpClient httpClient = CreateClient(
            MockService(accessResult: UserAccessRevokeResult.Succeeded(2, "Clients could not be notified.")));
        (string token, string cookie) =
            await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Users/Details?id=test-user&tab=access");
        var request =
            new HttpRequestMessage(HttpMethod.Post, "/Admin/Users/Details?id=test-user&handler=RevokeUserAccess");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        });

        HttpResponseMessage response = await httpClient.SendAsync(request);
        IDocument document = await GetDocumentAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(document.QuerySelector(".alert-success"));
        Assert.Contains("could not be notified", document.QuerySelector(".alert-warning")!.TextContent,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PostRevokeUserAccess_BlockedBySelfGuard_RedirectsWithError()
    {
        string blockReason =
            "You cannot revoke your own access from this page. Ask another administrator to do this if needed.";
        HttpClient httpClient = CreateClient(
            MockService(accessResult: UserAccessRevokeResult.Failed(blockReason)));
        (string token, string cookie) =
            await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Users/Details?id=test-user&tab=access");

        var request =
            new HttpRequestMessage(HttpMethod.Post, "/Admin/Users/Details?id=test-user&handler=RevokeUserAccess");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        });

        HttpResponseMessage response = await httpClient.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        IDocument document = await GetDocumentAsync(response);
        IElement? errorAlert = document.QuerySelector(".alert-error");
        Assert.NotNull(errorAlert);
        Assert.Contains("own", errorAlert!.TextContent);
    }

    [Fact]
    public async Task PostRevokeUserAccess_NonExistentUser_ReturnsNotFound()
    {
        var mock = new Mock<IUserDetailsService>();
        mock.Setup(s => s.GetUserDetailsAsync(It.IsAny<UserActionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserActionContext context, CancellationToken __) =>
                context.Target.Value == "non-existent" ? null : SampleUser(context.Target.Value));
        mock.Setup(s =>
                s.RevokeUserAccessAsync(It.Is<UserActionContext>(c => c.Target == UserId.Create("non-existent")),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(UserAccessRevokeResult.Failed("User not found."));

        HttpClient httpClient = CreateClient(mock.Object, false);
        (string token, string cookie) =
            await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Users/Details?id=test-user&tab=access");

        var request = new HttpRequestMessage(HttpMethod.Post,
            "/Admin/Users/Details?id=non-existent&handler=RevokeUserAccess");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        });

        HttpResponseMessage response = await httpClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostSuspendUser_ValidTarget_SuspendsAndRedirectsToDangerTab()
    {
        var mock = new Mock<IUserDetailsService>();
        mock.Setup(s => s.GetUserDetailsAsync(It.IsAny<UserActionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserActionContext context, CancellationToken __) => SampleUser(context.Target.Value));
        mock.Setup(s => s.SuspendUserAsync(It.Is<UserActionContext>(c => c.Target == UserId.Create("target-user")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(UserSuspendResult.Succeeded());

        HttpClient httpClient = CreateClient(mock.Object, false);
        (string token, string cookie) =
            await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Users/Details?id=target-user&tab=danger");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Users/Details?id=target-user&handler=Suspend");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        });

        HttpResponseMessage response = await httpClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("tab=danger", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task PostUnlockUser_ValidTarget_UnlocksAndRedirectsToOverviewTab()
    {
        var mock = new Mock<IUserDetailsService>();
        mock.Setup(s => s.GetUserDetailsAsync(It.IsAny<UserActionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserActionContext context, CancellationToken __) => SampleUser(context.Target.Value));
        mock.Setup(s => s.UnlockUserAsync(UserId.Create("target-user"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(UserUnlockResult.Succeeded);

        HttpClient httpClient = CreateClient(mock.Object, false);
        (string token, string cookie) =
            await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Users/Details?id=target-user&tab=overview");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Users/Details?id=target-user&handler=Unlock");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        });

        HttpResponseMessage response = await httpClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("tab=overview", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task PostDeleteUser_WithoutConfirmation_FailsAndRedirectsToDangerTab()
    {
        var mock = new Mock<IUserDetailsService>();
        mock.Setup(s => s.GetUserDetailsAsync(It.IsAny<UserActionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserActionContext context, CancellationToken __) => SampleUser(context.Target.Value));

        HttpClient httpClient = CreateClient(mock.Object, false);
        (string token, string cookie) =
            await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Users/Details?id=target-user&tab=danger");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Users/Details?id=target-user&handler=Delete");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        });

        HttpResponseMessage response = await httpClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("tab=danger", response.Headers.Location?.ToString());
        mock.Verify(s => s.DeleteUserAsync(It.IsAny<UserActionContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PostDeleteUser_ValidConfirmation_DeletesAndRedirectsToIndex()
    {
        var mock = new Mock<IUserDetailsService>();
        mock.Setup(s => s.GetUserDetailsAsync(It.IsAny<UserActionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserActionContext context, CancellationToken __) => SampleUser(context.Target.Value));
        mock.Setup(s => s.DeleteUserAsync(It.Is<UserActionContext>(c => c.Target == UserId.Create("target-user")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(UserDeleteResult.Succeeded());

        HttpClient httpClient = CreateClient(mock.Object, false);
        (string token, string cookie) =
            await ExtractAntiForgeryTokenAndCookieAsync(httpClient, "/Admin/Users/Details?id=target-user&tab=danger");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Admin/Users/Details?id=target-user&handler=Delete");
        request.Headers.Add("Cookie", cookie);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["DeleteConfirmation"] = "DELETE"
        });

        HttpResponseMessage response = await httpClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Admin/Users", response.Headers.Location?.ToString());
        mock.Verify(
            s => s.DeleteUserAsync(It.Is<UserActionContext>(c => c.Target == UserId.Create("target-user")),
                It.IsAny<CancellationToken>()), Times.Once);
    }
}