using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Entities;
using IdentityServerProject.Admin.Tests.Infrastructure;
using IdentityServerProject.Data;
using IdentityServerProject.Services.Apis;
using IdentityServerProject.Services.AuditLogs;
using IdentityServerProject.Services.Scopes;
using IdentityServerProject.Services.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityServerProject.Admin.Tests.Apis;

/// <summary>
///     Verifies TASK-04 audit coverage for <see cref="ApiResourceEditorService" />, which
///     previously audited every method's success path only.
/// </summary>
public class ApiResourceAuditCoverageTests : IClassFixture<AdminWebFactory>
{
    private readonly AdminWebFactory _factory;

    public ApiResourceAuditCoverageTests(AdminWebFactory factory)
    {
        _factory = factory;
    }

    private async Task SeedApiResourceAsync(ApiResource resource)
    {
        await _factory.RunInScopeAsync(async sp =>
        {
            ConfigurationDbContext configDb = sp.GetRequiredService<ConfigurationDbContext>();
            configDb.ApiResources.Add(resource);
            await configDb.SaveChangesAsync();
        });
    }

    private async Task<AuditLogEntry> GetSingleAuditEntryAsync(string action, string targetId)
    {
        AuditLogEntry? found = null;
        await _factory.RunInScopeAsync(async sp =>
        {
            ApplicationDbContext db = sp.GetRequiredService<ApplicationDbContext>();
            found = db.AuditLogEntries.Single(e =>
                e.Category == AuditCategory.ApiResource && e.Action == action && e.TargetId == targetId);
            await Task.CompletedTask;
        });
        return found!;
    }

    [Fact]
    public async Task DeleteAsync_ResourceNotFound_WritesDeniedAuditEvent()
    {
        string name = $"apires-audit-delete-missing-{Guid.NewGuid():N}";

        await _factory.RunInScopeAsync(async sp =>
        {
            IApiResourceEditorService service = sp.GetRequiredService<IApiResourceEditorService>();
            AdminMutationResult result = await service.DeleteAsync(ScopeName.Create(name));
            Assert.False(result.Succeeded);
        });

        AuditLogEntry entry = await GetSingleAuditEntryAsync(AuditAction.Delete, name);
        Assert.Equal(AuditOutcome.Denied, entry.Outcome);
        Assert.Equal(AuditReasonCode.NotFound, entry.ReasonCode);
    }

    [Fact]
    public async Task DeleteAsync_ResourceExists_WritesSucceededAuditEvent()
    {
        string name = $"apires-audit-delete-{Guid.NewGuid():N}";
        await SeedApiResourceAsync(new ApiResource { Name = name, DisplayName = "Delete Me", Enabled = true });

        await _factory.RunInScopeAsync(async sp =>
        {
            IApiResourceEditorService service = sp.GetRequiredService<IApiResourceEditorService>();
            AdminMutationResult result = await service.DeleteAsync(ScopeName.Create(name));
            Assert.True(result.Succeeded);
        });

        AuditLogEntry entry = await GetSingleAuditEntryAsync(AuditAction.Delete, name);
        Assert.Equal(AuditOutcome.Succeeded, entry.Outcome);
    }

    [Fact]
    public async Task SaveBasicsAsync_ValidationFailed_WritesDeniedAuditEvent()
    {
        string invalidName = "invalid name with spaces";

        await _factory.RunInScopeAsync(async sp =>
        {
            IApiResourceEditorService service = sp.GetRequiredService<IApiResourceEditorService>();
            SaveApiResourceBasicsResult result =
                await service.SaveBasicsAsync(new SaveApiResourceBasicsCommand(null, ScopeName.Create(invalidName),
                    "Display", "Desc"));
            Assert.Equal(AdminMutationStatus.ValidationFailed, result.Status);
        });

        AuditLogEntry entry = await GetSingleAuditEntryAsync(AuditAction.Create, invalidName);
        Assert.Equal(AuditOutcome.Denied, entry.Outcome);
        Assert.Equal(AuditReasonCode.ValidationFailed, entry.ReasonCode);
        Assert.Equal("API Resource validation failed.", entry.Details);
    }

    [Fact]
    public async Task SaveBasicsAsync_NameCollision_WritesDeniedAuditEvent()
    {
        string existingName = $"apires-audit-collision-existing-{Guid.NewGuid():N}";
        string newName = $"apires-audit-collision-new-{Guid.NewGuid():N}";
        await SeedApiResourceAsync(new ApiResource { Name = existingName, Enabled = true });
        await SeedApiResourceAsync(new ApiResource { Name = newName, Enabled = true });

        await _factory.RunInScopeAsync(async sp =>
        {
            IApiResourceEditorService service = sp.GetRequiredService<IApiResourceEditorService>();
            SaveApiResourceBasicsResult result = await service.SaveBasicsAsync(
                new SaveApiResourceBasicsCommand(ScopeName.Create(newName), ScopeName.Create(existingName), "Renamed",
                    null));
            Assert.Equal(AdminMutationStatus.Conflict, result.Status);
        });

        AuditLogEntry entry = await GetSingleAuditEntryAsync(AuditAction.UpdateBasics, existingName);
        Assert.Equal(AuditOutcome.Denied, entry.Outcome);
        Assert.Equal(AuditReasonCode.NameCollision, entry.ReasonCode);
    }

    [Fact]
    public async Task SaveBasicsAsync_CreateCollision_WritesDeniedAuditEvent()
    {
        string existingName = $"apires-audit-create-collision-{Guid.NewGuid():N}";
        await SeedApiResourceAsync(new ApiResource { Name = existingName, Enabled = true });

        await _factory.RunInScopeAsync(async sp =>
        {
            IApiResourceEditorService service = sp.GetRequiredService<IApiResourceEditorService>();
            SaveApiResourceBasicsResult result = await service.SaveBasicsAsync(
                new SaveApiResourceBasicsCommand(null, ScopeName.Create(existingName), "Duplicate", null));
            Assert.Equal(AdminMutationStatus.Conflict, result.Status);
        });

        AuditLogEntry entry = await GetSingleAuditEntryAsync(AuditAction.Create, existingName);
        Assert.Equal(AuditOutcome.Denied, entry.Outcome);
        Assert.Equal(AuditReasonCode.NameCollision, entry.ReasonCode);
    }

    [Fact]
    public async Task AddSecretAsync_ResourceNotFound_WritesDeniedAuditEvent()
    {
        string name = $"apires-audit-secret-missing-{Guid.NewGuid():N}";

        await _factory.RunInScopeAsync(async sp =>
        {
            IApiResourceEditorService service = sp.GetRequiredService<IApiResourceEditorService>();
            ApiResourceAddSecretResult result =
                await service.AddSecretAsync(new AddApiResourceSecretCommand(ScopeName.Create(name), "desc", null));
            Assert.Equal(AdminMutationStatus.NotFound, result.Status);
        });

        AuditLogEntry entry = await GetSingleAuditEntryAsync(AuditAction.GenerateSecret, name);
        Assert.Equal(AuditOutcome.Denied, entry.Outcome);
        Assert.Equal(AuditReasonCode.NotFound, entry.ReasonCode);
    }

    [Fact]
    public async Task SetEnabledAsync_ResourceExists_WritesSucceededAuditEvent()
    {
        string name = $"apires-audit-enable-{Guid.NewGuid():N}";
        await SeedApiResourceAsync(new ApiResource { Name = name, Enabled = true });

        await _factory.RunInScopeAsync(async sp =>
        {
            IApiResourceEditorService service = sp.GetRequiredService<IApiResourceEditorService>();
            AdminMutationResult result = await service.SetEnabledAsync(ScopeName.Create(name), false);
            Assert.True(result.Succeeded);
        });

        AuditLogEntry entry = await GetSingleAuditEntryAsync(AuditAction.SetEnabled, name);
        Assert.Equal(AuditOutcome.Succeeded, entry.Outcome);
    }

    [Fact]
    public async Task SetEnabledAsync_UnexpectedPersistenceFailure_WritesFailedAuditEventAndRethrows()
    {
        string name = $"apires-audit-failure-{Guid.NewGuid():N}";

        await Assert.ThrowsAnyAsync<Exception>(() => _factory.RunInScopeAsync(async sp =>
        {
            IApiResourceEditorService service = sp.GetRequiredService<IApiResourceEditorService>();
            await sp.GetRequiredService<ConfigurationDbContext>().DisposeAsync();
            await service.SetEnabledAsync(ScopeName.Create(name), false);
        }));

        AuditLogEntry entry = await GetSingleAuditEntryAsync(AuditAction.SetEnabled, name);
        Assert.Equal(AuditOutcome.Failed, entry.Outcome);
        Assert.Equal(AuditReasonCode.PersistenceFailure, entry.ReasonCode);
    }
}