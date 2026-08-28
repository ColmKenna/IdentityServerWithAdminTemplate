using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.Validation;
using IdentityServerProject.Services.AuditLogs;
using Microsoft.EntityFrameworkCore;
using Client = Duende.IdentityServer.EntityFramework.Entities.Client;

namespace IdentityServerProject.Services.Clients;

public partial class ClientDetailsService : IClientDetailsService
{
    private const string GrantTypeAuthorizationCode = "authorization_code";
    private const string GrantTypeClientCredentials = "client_credentials";
    private const string GrantTypeHybrid = "hybrid";
    private const string GrantTypeImplicit = "implicit";
    private const string GrantTypeDeviceCode = "urn:ietf:params:oauth:grant-type:device_code";

    public const string DisabledAtPropertyKey = "admin:disabledAt";
    public const int MinimumDisabledDaysBeforeDelete = 90;
    private readonly IAuditWriter _auditWriter;
    private readonly IClientConfigurationValidator _clientConfigurationValidator;

    private readonly ConfigurationDbContext _configurationDbContext;
    private readonly TimeProvider _timeProvider;

    public ClientDetailsService(
        ConfigurationDbContext configurationDbContext,
        IAuditWriter auditWriter,
        IClientConfigurationValidator clientConfigurationValidator,
        TimeProvider timeProvider)
    {
        _configurationDbContext = configurationDbContext;
        _auditWriter = auditWriter;
        _clientConfigurationValidator = clientConfigurationValidator;
        _timeProvider = timeProvider;
    }

    private async Task<Client?> LoadCompleteClientAsync(
        string clientId,
        bool asNoTracking,
        CancellationToken cancellationToken)
    {
        IQueryable<Client> query = _configurationDbContext.Clients
            .AsSplitQuery()
            .Include(client => client.AllowedGrantTypes)
            .Include(client => client.RedirectUris)
            .Include(client => client.PostLogoutRedirectUris)
            .Include(client => client.AllowedCorsOrigins)
            .Include(client => client.AllowedScopes)
            .Include(client => client.ClientSecrets)
            .Include(client => client.Claims)
            .Include(client => client.IdentityProviderRestrictions)
            .Include(client => client.Properties)
            .AsQueryable();

        if (asNoTracking)
            query = query.AsNoTracking();

        return await query.FirstOrDefaultAsync(client => client.ClientId == clientId, cancellationToken);
    }

    private async Task<string?> ValidateClientAsync(
        Duende.IdentityServer.Models.Client proposed,
        CancellationToken cancellationToken)
    {
        var validationContext = new ClientConfigurationValidationContext(proposed);
        await _clientConfigurationValidator.ValidateAsync(validationContext, cancellationToken);
        return validationContext.IsValid
            ? null
            : validationContext.ErrorMessage ?? "Invalid client configuration.";
    }

    private static void ReplaceCollection<TValue, TEntity>(
        ICollection<TEntity> target,
        IEnumerable<TValue> values,
        Func<TValue, TEntity> create)
    {
        target.Clear();
        foreach (TValue value in values) target.Add(create(value));
    }

    private Task AuditDeniedAsync(AuditAction action, AuditReasonCode reasonCode, string targetId, string targetName,
        string details, CancellationToken cancellationToken)
        => _auditWriter.WriteAsync(new AdminAuditEvent(
            AuditCategory.Client, action, AuditOutcome.Denied, reasonCode,
            targetId, targetName, Details: details), cancellationToken);

    private async Task<T> ExecuteAuditedAsync<T>(
        AuditAction action,
        string targetId,
        string targetName,
        Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        try
        {
            return await operation();
        }
        catch (Exception ex)
        {
            await AuditFailedAsync(action, targetId, targetName, ex, cancellationToken);
            throw;
        }
    }

    private async Task AuditFailedAsync(AuditAction action, string targetId, string targetName, Exception ex,
        CancellationToken cancellationToken)
    {
        const string marker = "IdentityServerProject.Audit.Client.Failed";
        if (ex.Data.Contains(marker))
            return;

        ex.Data[marker] = true;
        await _auditWriter.WriteAsync(new AdminAuditEvent(
            AuditCategory.Client, action, AuditOutcome.Failed, AuditReasonCode.PersistenceFailure,
            targetId, targetName, Details: $"Unexpected error ({ex.GetType().Name})"), cancellationToken);
    }
}