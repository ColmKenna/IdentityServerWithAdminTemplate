using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.Services;
using IdentityServerProject.Services.Users;
using Microsoft.Extensions.Logging;

namespace IdentityServerProject.Services.Diagnostics;

public class DiagnosticsService : IDiagnosticsService
{
    private readonly IIdentityDiagnosticsStore _identityStore;
    private readonly ConfigurationDbContext _configurationDbContext;
    private readonly PersistedGrantDbContext _persistedGrantDbContext;
    private readonly IKeyMaterialService _keyMaterialService;
    private readonly ReservedClaimTypePolicy _reservedClaimTypes;
    private readonly ILogger<DiagnosticsService> _logger;

    public DiagnosticsService(
        IIdentityDiagnosticsStore identityStore,
        ConfigurationDbContext configurationDbContext,
        PersistedGrantDbContext persistedGrantDbContext,
        IKeyMaterialService keyMaterialService,
        ReservedClaimTypePolicy reservedClaimTypes,
        ILogger<DiagnosticsService> logger)
    {
        _identityStore = identityStore;
        _configurationDbContext = configurationDbContext;
        _persistedGrantDbContext = persistedGrantDbContext;
        _keyMaterialService = keyMaterialService;
        _reservedClaimTypes = reservedClaimTypes;
        _logger = logger;
    }

    public async Task<DiagnosticsModel> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        var model = new DiagnosticsModel
        {
            StoreHealth =
            {
                await CheckStoreAsync("Identity Store", _identityStore.CanConnectAsync, _logger, cancellationToken),
                await CheckStoreAsync("Configuration Store", _configurationDbContext.Database.CanConnectAsync, _logger, cancellationToken),
                await CheckStoreAsync("Operational Store", _persistedGrantDbContext.Database.CanConnectAsync, _logger, cancellationToken)
            }
        };

        try
        {
            var signingCredential = await _keyMaterialService.GetSigningCredentialsAsync(allowedAlgorithms: null, cancellationToken);
            model.SigningKeyId = signingCredential?.Key.KeyId;
            model.SigningAlgorithm = signingCredential?.Algorithm;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load signing credentials from key material service.");
            model.SigningKeyId = "(error)";
            model.SigningAlgorithm = "(error)";
        }

        try
        {
            var validationKeys = await _keyMaterialService.GetValidationKeysAsync(cancellationToken);
            model.ActiveValidationKeys = validationKeys
                .Select(k => new SigningKeySummary
                {
                    KeyId = k.Key.KeyId ?? "(unknown)",
                    Algorithm = k.SigningAlgorithm,
                    IsX509Certificate = k.Key is Microsoft.IdentityModel.Tokens.X509SecurityKey
                })
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load validation keys from key material service.");
            model.ActiveValidationKeys = new List<SigningKeySummary>();
        }

        try
        {
            model.ReservedClaimHolders = await FindReservedClaimHoldersAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query reserved claim holders.");
            model.ReservedClaimHolders = new List<ReservedClaimHolder>();
        }

        return model;
    }

    /// <summary>
    /// Reports user claims whose type the admin claim editor refuses to create — claims written
    /// before that guard existed, or outside the admin UI.
    /// </summary>
    /// <remarks>
    /// The reserved check is ordinal case-insensitive with a prefix rule, which no provider
    /// translates to SQL reliably (SQLite compares strings case-sensitively by default, SQL Server
    /// follows the column collation). So the distinct claim types are read first — a small set
    /// whatever the row count — filtered in memory against the authoritative policy, and only the
    /// matching types are then fetched with their holders.
    /// </remarks>
    private async Task<List<ReservedClaimHolder>> FindReservedClaimHoldersAsync(CancellationToken cancellationToken)
    {
        var distinctClaimTypes = await _identityStore.GetDistinctUserClaimTypesAsync(cancellationToken);

        var reservedClaimTypes = distinctClaimTypes
            .Where(t => _reservedClaimTypes.IsReserved(t))
            .ToList();

        if (reservedClaimTypes.Count == 0)
        {
            return new List<ReservedClaimHolder>();
        }

        var holders = await _identityStore.GetClaimHoldersAsync(reservedClaimTypes, cancellationToken);
        return holders.ToList();
    }

    private static async Task<StoreHealthStatus> CheckStoreAsync(
        string name,
        Func<CancellationToken, Task<bool>> canConnect,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var connected = await canConnect(cancellationToken);
            return new StoreHealthStatus
            {
                Name = name,
                IsHealthy = connected,
                Detail = connected ? "Connected" : "Unable to connect to the store."
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception occurred while checking connectivity for store '{StoreName}'.", name);
            return new StoreHealthStatus
            {
                Name = name,
                IsHealthy = false,
                Detail = "An error occurred while checking store health."
            };
        }
    }
}
