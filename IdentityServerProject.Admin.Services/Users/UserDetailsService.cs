using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.Services;
using IdentityServerProject.Services.AuditLogs;
using Microsoft.AspNetCore.Http;

namespace IdentityServerProject.Services.Users;

public partial class UserDetailsService(
    IIdentityUserAdministrationStore store,
    PersistedGrantDbContext persistedGrantDbContext,
    ReservedClaimTypePolicy reservedClaimTypes,
    IBackChannelLogoutService backChannelLogoutService,
    IAuditWriter auditWriter,
    IHttpContextAccessor httpContextAccessor) : IUserDetailsService
{
    private readonly IAuditWriter _auditWriter = auditWriter;
    private readonly IBackChannelLogoutService _backChannelLogoutService = backChannelLogoutService;
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;
    private readonly PersistedGrantDbContext _persistedGrantDbContext = persistedGrantDbContext;
    private readonly ReservedClaimTypePolicy _reservedClaimTypes = reservedClaimTypes;
    private readonly IIdentityUserAdministrationStore _store = store;

}