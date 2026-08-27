using System.Reflection;
using IdentityServerProject.Data;
using IdentityServerProject.Services.AuditLogs;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IdentityServerProject.Admin.Tests.AuditLogs;

/// <summary>
///     TASK-04 architecture/inventory test: every mutating <c>OnPost*</c> handler under
///     <c>Pages/Admin</c> must delegate to a service method, and no PageModel may write audit
///     entries itself (services own audit events because they own the outcome and invariant).
///     This is a maintained allow-list, not a fully automatic call-graph check: adding a new
///     <c>OnPost*</c> handler that isn't in <see cref="ExpectedMutatingHandlers" /> fails this test,
///     forcing the author to confirm the delegated service method is audited (per the per-service
///     coverage tests in this project) before adding it to the list.
/// </summary>
public class PageModelAuditCoverageTests
{
    /// <summary>
    ///     (PageModel full type name, OnPost handler name) pairs confirmed to delegate to an
    ///     audited service method. Derived directly from Pages/Admin as of this task.
    /// </summary>
    private static readonly HashSet<(string TypeFullName, string Handler)> ExpectedMutatingHandlers = new()
    {
        ("IdentityServerProject.Pages.Admin.Users.IndexModel", "OnPostUnlockAsync"),
        ("IdentityServerProject.Pages.Admin.Users.DetailsModel", "OnPostUnlockAsync"),
        ("IdentityServerProject.Pages.Admin.Users.DetailsModel", "OnPostAddRoleAsync"),
        ("IdentityServerProject.Pages.Admin.Users.DetailsModel", "OnPostRemoveRoleAsync"),
        ("IdentityServerProject.Pages.Admin.Users.DetailsModel", "OnPostAddClaimAsync"),
        ("IdentityServerProject.Pages.Admin.Users.DetailsModel", "OnPostRemoveClaimAsync"),
        ("IdentityServerProject.Pages.Admin.Users.DetailsModel", "OnPostRevokeUserAccessAsync"),
        ("IdentityServerProject.Pages.Admin.Users.DetailsModel", "OnPostSuspendAsync"),
        ("IdentityServerProject.Pages.Admin.Users.DetailsModel", "OnPostDeleteAsync"),
        ("IdentityServerProject.Pages.Admin.Users.CreateModel", "OnPostAsync"),
        ("IdentityServerProject.Pages.Admin.Users.ResetPasswordModel", "OnPostAsync"),

        ("IdentityServerProject.Pages.Admin.Roles.CreateModel", "OnPostAsync"),
        ("IdentityServerProject.Pages.Admin.Roles.IndexModel", "OnPostDeleteAsync"),

        ("IdentityServerProject.Pages.Admin.Grants.IndexModel", "OnPostRevokeAsync"),

        ("IdentityServerProject.Pages.Admin.Clients.BasicsModel", "OnPostAsync"),
        ("IdentityServerProject.Pages.Admin.Clients.DetailsModel", "OnPostToggleStatusAsync"),
        ("IdentityServerProject.Pages.Admin.Clients.DetailsModel", "OnPostDeleteAsync"),
        ("IdentityServerProject.Pages.Admin.Clients.PermissionsModel", "OnPostAsync"),
        ("IdentityServerProject.Pages.Admin.Clients.TokenSettingsModel", "OnPostAsync"),
        ("IdentityServerProject.Pages.Admin.Clients.AuthenticationModel", "OnPostAsync"),
        ("IdentityServerProject.Pages.Admin.Clients.SecretsModel", "OnPostGenerateAsync"),
        ("IdentityServerProject.Pages.Admin.Clients.SecretsModel", "OnPostRevokeAsync"),
        ("IdentityServerProject.Pages.Admin.Clients.CreateModel", "OnPostAsync"),
        ("IdentityServerProject.Pages.Admin.Clients.CloneModel", "OnPostAsync"),

        ("IdentityServerProject.Pages.Admin.ApiScopes.EditModel", "OnPostSaveAsync"),
        ("IdentityServerProject.Pages.Admin.ApiScopes.EditModel", "OnPostAddClaimAsync"),
        ("IdentityServerProject.Pages.Admin.ApiScopes.EditModel", "OnPostRemoveClaimAsync"),
        ("IdentityServerProject.Pages.Admin.ApiScopes.CreateModel", "OnPostAsync"),
        ("IdentityServerProject.Pages.Admin.ApiScopes.IndexModel", "OnPostDeleteAsync"),

        ("IdentityServerProject.Pages.Admin.IdentityResources.EditModel", "OnPostSaveAsync"),
        ("IdentityServerProject.Pages.Admin.IdentityResources.EditModel", "OnPostAddClaimAsync"),
        ("IdentityServerProject.Pages.Admin.IdentityResources.EditModel", "OnPostRemoveClaimAsync"),
        ("IdentityServerProject.Pages.Admin.IdentityResources.CreateModel", "OnPostAsync"),
        ("IdentityServerProject.Pages.Admin.IdentityResources.IndexModel", "OnPostDeleteAsync"),

        ("IdentityServerProject.Pages.Admin.Apis.EditorModel", "OnPostSaveBasicsAsync"),
        ("IdentityServerProject.Pages.Admin.Apis.EditorModel", "OnPostAddSecretAsync"),
        ("IdentityServerProject.Pages.Admin.Apis.EditorModel", "OnPostRevokeSecretAsync"),
        ("IdentityServerProject.Pages.Admin.Apis.EditorModel", "OnPostAttachScopeAsync"),
        ("IdentityServerProject.Pages.Admin.Apis.EditorModel", "OnPostCreateScopeAsync"),
        ("IdentityServerProject.Pages.Admin.Apis.EditorModel", "OnPostDetachScopeAsync"),
        ("IdentityServerProject.Pages.Admin.Apis.EditorModel", "OnPostAddClaimAsync"),
        ("IdentityServerProject.Pages.Admin.Apis.EditorModel", "OnPostRemoveClaimAsync"),
        ("IdentityServerProject.Pages.Admin.Apis.EditorModel", "OnPostEnableAsync"),
        ("IdentityServerProject.Pages.Admin.Apis.EditorModel", "OnPostDisableAsync"),
        ("IdentityServerProject.Pages.Admin.Apis.EditorModel", "OnPostDeleteAsync")
    };

    /// <summary>
    ///     PageModels with zero OnPost* handlers today (pure read/filter pages). Listed explicitly
    ///     so their absence from <see cref="ExpectedMutatingHandlers" /> reads as intentional.
    /// </summary>
    private static readonly HashSet<string> KnownNonMutatingPageModels = new()
    {
        "IdentityServerProject.Pages.Admin.Clients.IndexModel",
        "IdentityServerProject.Pages.Admin.Apis.IndexModel",
        "IdentityServerProject.Pages.Admin.AuditLogs.IndexModel",
        "IdentityServerProject.Pages.Admin.Diagnostics.IndexModel"
    };

    private static IEnumerable<Type> GetAdminPageModelTypes()
    {
        return typeof(ApplicationDbContext).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract)
            .Where(t => typeof(PageModel).IsAssignableFrom(t))
            .Where(t => t.Namespace != null &&
                        t.Namespace.StartsWith("IdentityServerProject.Pages.Admin", StringComparison.Ordinal));
    }

    private static IEnumerable<MethodInfo> GetOnPostHandlers(Type pageModelType)
    {
        return pageModelType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.Name.StartsWith("OnPost", StringComparison.Ordinal));
    }

    [Fact]
    public void EveryOnPostHandlerUnderPagesAdmin_IsOnTheAuditedAllowList()
    {
        var actualHandlers = GetAdminPageModelTypes()
            .SelectMany(t => GetOnPostHandlers(t).Select(m => (TypeFullName: t.FullName!, Handler: m.Name)))
            .ToHashSet();

        var missingFromAllowList = actualHandlers.Except(ExpectedMutatingHandlers).ToList();
        Assert.True(missingFromAllowList.Count == 0,
            "Found OnPost* handler(s) not on the audited allow-list (add them to ExpectedMutatingHandlers " +
            "only after confirming the delegated service method audits Succeeded/Denied/Failed outcomes): " +
            string.Join(", ", missingFromAllowList.Select(h => $"{h.TypeFullName}.{h.Handler}")));

        var removedFromCode = ExpectedMutatingHandlers.Except(actualHandlers).ToList();
        Assert.True(removedFromCode.Count == 0,
            "Allow-listed handler(s) no longer exist in the codebase - remove them from ExpectedMutatingHandlers: " +
            string.Join(", ", removedFromCode.Select(h => $"{h.TypeFullName}.{h.Handler}")));
    }

    [Fact]
    public void NoPageModelUnderPagesAdmin_HasADirectIAuditWriterDependency()
    {
        // PageModels must not audit directly - only services own audit events, since only
        // services know the outcome and invariant behind a mutation.
        var offenders = GetAdminPageModelTypes()
            .Where(t => t.GetConstructors()
                .SelectMany(c => c.GetParameters())
                .Any(p => p.ParameterType == typeof(IAuditWriter)))
            .Select(t => t.FullName)
            .ToList();

        Assert.True(offenders.Count == 0,
            "PageModel(s) inject IAuditWriter directly - auditing belongs in the service layer: " +
            string.Join(", ", offenders));
    }

    [Fact]
    public void KnownNonMutatingPageModels_StillHaveNoOnPostHandlers()
    {
        foreach (string typeName in KnownNonMutatingPageModels)
        {
            Type? type = GetAdminPageModelTypes().SingleOrDefault(t => t.FullName == typeName);
            Assert.True(type != null,
                $"Expected PageModel '{typeName}' was not found - update KnownNonMutatingPageModels.");

            var handlers = GetOnPostHandlers(type!).ToList();
            Assert.True(handlers.Count == 0,
                $"'{typeName}' was documented as non-mutating but now has OnPost handler(s): {string.Join(", ", handlers.Select(m => m.Name))}. " +
                "Move it into ExpectedMutatingHandlers once its delegated service method is confirmed audited.");
        }
    }
}