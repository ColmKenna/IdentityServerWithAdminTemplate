using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.Services;
using IdentityServerProject.Services.AuditLogs;
using Microsoft.AspNetCore.Http;

namespace IdentityServerProject.Services.Users;

public partial class UserDetailsService : IUserDetailsService
{
    private readonly IAuditWriter _auditWriter;
    private readonly IBackChannelLogoutService _backChannelLogoutService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly PersistedGrantDbContext _persistedGrantDbContext;
    private readonly ReservedClaimTypePolicy _reservedClaimTypes;
    private readonly IIdentityUserAdministrationStore _store;

    public UserDetailsService(
        IIdentityUserAdministrationStore store,
        PersistedGrantDbContext persistedGrantDbContext,
        ReservedClaimTypePolicy reservedClaimTypes,
        IBackChannelLogoutService backChannelLogoutService,
        IAuditWriter auditWriter,
        IHttpContextAccessor httpContextAccessor)
    {
        _store = store;
        _persistedGrantDbContext = persistedGrantDbContext;
        _reservedClaimTypes = reservedClaimTypes;
        _backChannelLogoutService = backChannelLogoutService;
        _auditWriter = auditWriter;
        _httpContextAccessor = httpContextAccessor;
    }
}