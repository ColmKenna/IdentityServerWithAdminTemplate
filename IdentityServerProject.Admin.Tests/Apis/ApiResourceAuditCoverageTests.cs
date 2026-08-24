using IdentityServerProject.Admin.Tests.Infrastructure;
using System;
using System.Linq;
using System.Threading.Tasks;
using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Entities;
using IdentityServerProject.Data;
using IdentityServerProject.Services.Apis;
using IdentityServerProject.Services.AuditLogs;
using IdentityServerProject.Services.Validation;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IdentityServerProject.Admin.Tests.Apis;

/// <summary>
/// Verifies TASK-04 audit coverage for <see cref="ApiResourceEditorService"/>, which
/// previously audited every method's success path only.
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
            var configDb = sp.GetRequiredService<ConfigurationDbContext>();
            configDb.ApiResources.Add(resource);
            await configDb.SaveChangesAsync();
        });
    }

    private async Task<AuditLogEntry> GetSingleAuditEntryAsync(string action, string targetId)
    {
        AuditLogEntry? found = null;
        await _factory.RunInScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            found = db.AuditLogEntries.Single(e => e.Category == AuditCategories.ApiResource && e.Action == action && e.TargetId == targetId);
            await Task.CompletedTask;
        });
        return found!;
    }

    [Fact]
    public async Task DeleteAsync_ResourceNotFound_WritesDeniedAuditEvent()
    {
        var name = $"apires-audit-delete-missing-{Guid.NewGuid():N}";

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiResourceEditorService>();
            var result = await service.DeleteAsync(name);
            Assert.False(result.Succeeded);
        });

        var entry = await GetSingleAuditEntryAsync(AuditActions.Delete, name);
        Assert.Equal(AuditOutcome.Denied, entry.Outcome);
        Assert.Equal(AuditReasonCodes.NotFound, entry.ReasonCode);
    }

    [Fact]
    public async Task DeleteAsync_ResourceExists_WritesSucceededAuditEvent()
    {
        var name = $"apires-audit-delete-{Guid.NewGuid():N}";
        await SeedApiResourceAsync(new ApiResource { Name = name, DisplayName = "Delete Me", Enabled = true });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiResourceEditorService>();
            var result = await service.DeleteAsync(name);
            Assert.True(result.Succeeded);
        });

        var entry = await GetSingleAuditEntryAsync(AuditActions.Delete, name);
        Assert.Equal(AuditOutcome.Succeeded, entry.Outcome);
    }

    [Fact]
    public async Task SaveBasicsAsync_ValidationFailed_WritesDeniedAuditEvent()
    {
        var invalidName = "invalid name with spaces";

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiResourceEditorService>();
            var result = await service.SaveBasicsAsync(new SaveApiResourceBasicsCommand(null, invalidName, "Display", "Desc"));
            Assert.Equal(AdminMutationStatus.ValidationFailed, result.Status);
        });

        var entry = await GetSingleAuditEntryAsync(AuditActions.Create, invalidName);
        Assert.Equal(AuditOutcome.Denied, entry.Outcome);
        Assert.Equal(AuditReasonCodes.ValidationFailed, entry.ReasonCode);
        Assert.Equal("API Resource validation failed.", entry.Details);
    }

    [Fact]
    public async Task SaveBasicsAsync_NameCollision_WritesDeniedAuditEvent()
    {
        var existingName = $"apires-audit-collision-existing-{Guid.NewGuid():N}";
        var newName = $"apires-audit-collision-new-{Guid.NewGuid():N}";
        await SeedApiResourceAsync(new ApiResource { Name = existingName, Enabled = true });
        await SeedApiResourceAsync(new ApiResource { Name = newName, Enabled = true });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiResourceEditorService>();
            var result = await service.SaveBasicsAsync(new SaveApiResourceBasicsCommand(newName, existingName, "Renamed", null));
            Assert.Equal(AdminMutationStatus.Conflict, result.Status);
        });

        var entry = await GetSingleAuditEntryAsync(AuditActions.UpdateBasics, existingName);
        Assert.Equal(AuditOutcome.Denied, entry.Outcome);
        Assert.Equal(AuditReasonCodes.NameCollision, entry.ReasonCode);
    }

    [Fact]
    public async Task SaveBasicsAsync_CreateCollision_WritesDeniedAuditEvent()
    {
        var existingName = $"apires-audit-create-collision-{Guid.NewGuid():N}";
        await SeedApiResourceAsync(new ApiResource { Name = existingName, Enabled = true });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiResourceEditorService>();
            var result = await service.SaveBasicsAsync(new SaveApiResourceBasicsCommand(null, existingName, "Duplicate", null));
            Assert.Equal(AdminMutationStatus.Conflict, result.Status);
        });

        var entry = await GetSingleAuditEntryAsync(AuditActions.Create, existingName);
        Assert.Equal(AuditOutcome.Denied, entry.Outcome);
        Assert.Equal(AuditReasonCodes.NameCollision, entry.ReasonCode);
    }

    [Fact]
    public async Task AddSecretAsync_ResourceNotFound_WritesDeniedAuditEvent()
    {
        var name = $"apires-audit-secret-missing-{Guid.NewGuid():N}";

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiResourceEditorService>();
            var result = await service.AddSecretAsync(new AddApiResourceSecretCommand(name, "desc", null));
            Assert.Equal(AdminMutationStatus.NotFound, result.Status);
        });

        var entry = await GetSingleAuditEntryAsync(AuditActions.GenerateSecret, name);
        Assert.Equal(AuditOutcome.Denied, entry.Outcome);
        Assert.Equal(AuditReasonCodes.NotFound, entry.ReasonCode);
    }

    [Fact]
    public async Task SetEnabledAsync_ResourceExists_WritesSucceededAuditEvent()
    {
        var name = $"apires-audit-enable-{Guid.NewGuid():N}";
        await SeedApiResourceAsync(new ApiResource { Name = name, Enabled = true });

        await _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiResourceEditorService>();
            var result = await service.SetEnabledAsync(name, false);
            Assert.True(result.Succeeded);
        });

        var entry = await GetSingleAuditEntryAsync(AuditActions.SetEnabled, name);
        Assert.Equal(AuditOutcome.Succeeded, entry.Outcome);
    }

    [Fact]
    public async Task SetEnabledAsync_UnexpectedPersistenceFailure_WritesFailedAuditEventAndRethrows()
    {
        var name = $"apires-audit-failure-{Guid.NewGuid():N}";

        await Assert.ThrowsAnyAsync<Exception>(() => _factory.RunInScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IApiResourceEditorService>();
            await sp.GetRequiredService<ConfigurationDbContext>().DisposeAsync();
            await service.SetEnabledAsync(name, false);
        }));

        var entry = await GetSingleAuditEntryAsync(AuditActions.SetEnabled, name);
        Assert.Equal(AuditOutcome.Failed, entry.Outcome);
        Assert.Equal(AuditReasonCodes.PersistenceFailure, entry.ReasonCode);
    }
}
