using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Entities;
using IdentityServerProject.Admin.Tests.Infrastructure;
using IdentityServerProject.Services.Apis;
using IdentityServerProject.Services.AuditLogs;
using IdentityServerProject.Services.Scopes;
using IdentityServerProject.Services.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace IdentityServerProject.Admin.Tests.Apis;

public class ApiResourceValidationTests : IClassFixture<AdminWebFactory>
{
    private readonly AdminWebFactory _factory;

    public ApiResourceValidationTests(AdminWebFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SaveBasicsAsync_WhitespaceOrInvalidIdentifier_FailsValidation()
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            IApiResourceEditorService service = sp.GetRequiredService<IApiResourceEditorService>();

            SaveApiResourceBasicsResult whitespaceResult =
                await service.SaveBasicsAsync(new SaveApiResourceBasicsCommand(null, ScopeName.Create("   "), "Display",
                    "Desc"));
            Assert.Equal(AdminMutationStatus.ValidationFailed, whitespaceResult.Status);
            Assert.True(whitespaceResult.Errors.ContainsKey("Basics.Name"));

            SaveApiResourceBasicsResult invalidCharResult = await service.SaveBasicsAsync(
                new SaveApiResourceBasicsCommand(null, ScopeName.Create("invalid name with spaces"), "Display",
                    "Desc"));
            Assert.Equal(AdminMutationStatus.ValidationFailed, invalidCharResult.Status);
            Assert.True(invalidCharResult.Errors.ContainsKey("Basics.Name"));
        });
    }

    [Fact]
    public async Task SaveBasicsAsync_OverlongName_FailsValidation()
    {
        string overlongName = new('a', ValidationConstants.MaxNameLength + 1);

        await _factory.RunInScopeAsync(async sp =>
        {
            IApiResourceEditorService service = sp.GetRequiredService<IApiResourceEditorService>();

            SaveApiResourceBasicsResult result = await service.SaveBasicsAsync(
                new SaveApiResourceBasicsCommand(null, ScopeName.Create(overlongName), "Display", "Desc"));

            Assert.Equal(AdminMutationStatus.ValidationFailed, result.Status);
            Assert.True(result.Errors.ContainsKey("Basics.Name"));
            Assert.Contains(result.Errors["Basics.Name"],
                msg => msg.Contains($"cannot exceed {ValidationConstants.MaxNameLength} characters"));
        });
    }

    [Fact]
    public async Task SaveBasicsAsync_OverlongDisplayName_FailsValidation()
    {
        string overlongDisplayName = new('a', ValidationConstants.MaxDisplayNameLength + 1);

        await _factory.RunInScopeAsync(async sp =>
        {
            IApiResourceEditorService service = sp.GetRequiredService<IApiResourceEditorService>();

            SaveApiResourceBasicsResult result = await service.SaveBasicsAsync(
                new SaveApiResourceBasicsCommand(null, ScopeName.Create("valid-name"), overlongDisplayName, "Desc"));

            Assert.Equal(AdminMutationStatus.ValidationFailed, result.Status);
            Assert.True(result.Errors.ContainsKey("Basics.DisplayName"));
            Assert.Contains(result.Errors["Basics.DisplayName"],
                msg => msg.Contains($"cannot exceed {ValidationConstants.MaxDisplayNameLength} characters"));
        });
    }

    [Fact]
    public async Task SaveBasicsAsync_OverlongDescription_FailsValidation()
    {
        string overlongDescription = new('a', ValidationConstants.MaxDescriptionLength + 1);

        await _factory.RunInScopeAsync(async sp =>
        {
            IApiResourceEditorService service = sp.GetRequiredService<IApiResourceEditorService>();

            SaveApiResourceBasicsResult result = await service.SaveBasicsAsync(
                new SaveApiResourceBasicsCommand(null, ScopeName.Create("valid-name"), "Display", overlongDescription));

            Assert.Equal(AdminMutationStatus.ValidationFailed, result.Status);
            Assert.True(result.Errors.ContainsKey("Basics.Description"));
            Assert.Contains(result.Errors["Basics.Description"],
                msg => msg.Contains($"cannot exceed {ValidationConstants.MaxDescriptionLength} characters"));
        });
    }

    [Fact]
    public async Task SaveBasicsAsync_MultipleValidationErrors_AllErrorsReported()
    {
        string overlongName = new('a', ValidationConstants.MaxNameLength + 1);
        string overlongDisplayName = new('b', ValidationConstants.MaxDisplayNameLength + 1);
        string overlongDescription = new('c', ValidationConstants.MaxDescriptionLength + 1);

        await _factory.RunInScopeAsync(async sp =>
        {
            IApiResourceEditorService service = sp.GetRequiredService<IApiResourceEditorService>();

            SaveApiResourceBasicsResult result = await service.SaveBasicsAsync(new SaveApiResourceBasicsCommand(null,
                ScopeName.Create(overlongName), overlongDisplayName, overlongDescription));

            Assert.Equal(AdminMutationStatus.ValidationFailed, result.Status);
            Assert.True(result.Errors.ContainsKey("Basics.Name"));
            Assert.True(result.Errors.ContainsKey("Basics.DisplayName"));
            Assert.True(result.Errors.ContainsKey("Basics.Description"));
        });
    }

    [Fact]
    public async Task SaveBasicsAsync_ExistingResourceInvalidBasics_DoesNotPersistChanges()
    {
        string tag = Guid.NewGuid().ToString("N");
        string name = $"{tag}-valid-api";
        string originalDisplay = "Original Display";

        await _factory.RunInScopeAsync(async sp =>
        {
            ConfigurationDbContext db = sp.GetRequiredService<ConfigurationDbContext>();
            db.ApiResources.Add(new ApiResource { Name = name, DisplayName = originalDisplay, Enabled = true });
            await db.SaveChangesAsync();
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            IApiResourceEditorService service = sp.GetRequiredService<IApiResourceEditorService>();

            string overlongName = new('a', ValidationConstants.MaxNameLength + 1);
            SaveApiResourceBasicsResult result = await service.SaveBasicsAsync(
                new SaveApiResourceBasicsCommand(ScopeName.Create(name), ScopeName.Create(overlongName),
                    "Updated Display", "Updated Desc"));

            Assert.Equal(AdminMutationStatus.ValidationFailed, result.Status);
            Assert.True(result.Errors.ContainsKey("Basics.Name"));

            ApiResourceEditorModel? editor = await service.GetForEditAsync(ScopeName.Create(name));
            Assert.NotNull(editor);
            Assert.Equal(originalDisplay, editor!.DisplayName);
        });
    }


    [Fact]
    public async Task AddSecretAsync_PastOrCurrentExpiration_RejectedWithTimeProvider()
    {
        string tag = Guid.NewGuid().ToString("N");
        string name = $"{tag}-api";
        var now = new DateTimeOffset(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);
        var fakeTime = new FakeTimeProvider(now);

        await _factory.RunInScopeAsync(async sp =>
        {
            ConfigurationDbContext db = sp.GetRequiredService<ConfigurationDbContext>();
            db.ApiResources.Add(new ApiResource { Name = name, Enabled = true });
            await db.SaveChangesAsync();
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            ConfigurationDbContext db = sp.GetRequiredService<ConfigurationDbContext>();
            IAuditWriter auditWriter = sp.GetRequiredService<IAuditWriter>();
            var service = new ApiResourceEditorService(db, auditWriter, fakeTime);

            // Past expiration
            ApiResourceAddSecretResult pastResult = await service.AddSecretAsync(
                new AddApiResourceSecretCommand(ScopeName.Create(name), "desc", now.AddMinutes(-5).UtcDateTime));
            Assert.Equal(AdminMutationStatus.ValidationFailed, pastResult.Status);
            Assert.True(pastResult.Errors.ContainsKey("Secret.Expiration"));

            // Exact current time
            ApiResourceAddSecretResult currentResult =
                await service.AddSecretAsync(new AddApiResourceSecretCommand(ScopeName.Create(name), "desc",
                    now.UtcDateTime));
            Assert.Equal(AdminMutationStatus.ValidationFailed, currentResult.Status);
            Assert.True(currentResult.Errors.ContainsKey("Secret.Expiration"));

            // Strictly future expiration
            ApiResourceAddSecretResult futureResult = await service.AddSecretAsync(
                new AddApiResourceSecretCommand(ScopeName.Create(name), "desc", now.AddMinutes(5).UtcDateTime));
            Assert.Equal(AdminMutationStatus.Succeeded, futureResult.Status);
            Assert.True(futureResult.Success);
        });
    }

    [Fact]
    public async Task CreateScopeAsync_CollisionWithIdentityResource_FailsValidation()
    {
        string tag = Guid.NewGuid().ToString("N");
        string name = $"{tag}-api";
        string scopeName = $"{tag}-identity-scope";

        await _factory.RunInScopeAsync(async sp =>
        {
            ConfigurationDbContext db = sp.GetRequiredService<ConfigurationDbContext>();
            db.ApiResources.Add(new ApiResource { Name = name, Enabled = true });
            db.IdentityResources.Add(new IdentityResource { Name = scopeName, Enabled = true });
            await db.SaveChangesAsync();
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            IApiResourceEditorService service = sp.GetRequiredService<IApiResourceEditorService>();

            AdminMutationResult result =
                await service.CreateScopeAsync(
                    new CreateApiResourceScopeCommand(ScopeName.Create(name), scopeName, "Display"));

            Assert.Equal(AdminMutationStatus.Conflict, result.Status);
            Assert.True(result.Errors.ContainsKey("CreateScope.ScopeName"));
        });
    }

    [Fact]
    public async Task AddClaimAsync_OverlongClaimType_FailsValidation()
    {
        string tag = Guid.NewGuid().ToString("N");
        string name = $"{tag}-api";
        string overlongClaim = new('c', ValidationConstants.MaxClaimTypeLength + 1);

        await _factory.RunInScopeAsync(async sp =>
        {
            ConfigurationDbContext db = sp.GetRequiredService<ConfigurationDbContext>();
            db.ApiResources.Add(new ApiResource { Name = name, Enabled = true });
            await db.SaveChangesAsync();
        });

        await _factory.RunInScopeAsync(async sp =>
        {
            IApiResourceEditorService service = sp.GetRequiredService<IApiResourceEditorService>();

            AdminMutationResult result =
                await service.AddClaimAsync(new AddApiResourceClaimCommand(ScopeName.Create(name),
                    ClaimType.Create(overlongClaim)));

            Assert.Equal(AdminMutationStatus.ValidationFailed, result.Status);
            Assert.True(result.Errors.ContainsKey("Claim.ClaimType"));
        });
    }

    [Fact]
    public async Task NestedCommands_WhitespaceResourceOrValues_AreRejectedAtServiceBoundary()
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            IApiResourceEditorService service = sp.GetRequiredService<IApiResourceEditorService>();

            ApiResourceAddSecretResult secret =
                await service.AddSecretAsync(new AddApiResourceSecretCommand(ScopeName.Create("   "), "description",
                    null));
            Assert.Equal(AdminMutationStatus.ValidationFailed, secret.Status);
            Assert.True(secret.Errors.ContainsKey("Name"));

            AdminMutationResult scope =
                await service.CreateScopeAsync(
                    new CreateApiResourceScopeCommand(ScopeName.Create("   "), "   ", "Display"));
            Assert.Equal(AdminMutationStatus.ValidationFailed, scope.Status);
            Assert.True(scope.Errors.ContainsKey("Name"));
            Assert.True(scope.Errors.ContainsKey("CreateScope.ScopeName"));

            AdminMutationResult claim =
                await service.AddClaimAsync(new AddApiResourceClaimCommand(ScopeName.Create("   "),
                    ClaimType.Create("   ")));
            Assert.Equal(AdminMutationStatus.ValidationFailed, claim.Status);
            Assert.True(claim.Errors.ContainsKey("Name"));
            Assert.True(claim.Errors.ContainsKey("Claim.ClaimType"));
        });
    }
}