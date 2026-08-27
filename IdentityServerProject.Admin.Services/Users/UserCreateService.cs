using IdentityServerProject.Services.AuditLogs;

namespace IdentityServerProject.Services.Users;

public class UserCreateService : IUserCreateService
{
    private readonly IAuditWriter _auditWriter;
    private readonly IIdentityUserAdministrationStore _store;

    public UserCreateService(IIdentityUserAdministrationStore store, IAuditWriter auditWriter)
    {
        _store = store;
        _auditWriter = auditWriter;
    }

    public async Task<UserCreateResult> CreateUserAsync(UserCreateInputModel input,
        CancellationToken cancellationToken = default)
    {
        string userName = input.UserName?.Trim() ?? string.Empty;

        try
        {
            UserCreateOutcome outcome = await _store.CreateUserAsync(input, cancellationToken);
            UserCreateResult result = outcome.Result;
            AuditReasonCode reasonCode = outcome.ReasonCode;

            if (!result.Success)
            {
                await _auditWriter.WriteAsync(new AdminAuditEvent(
                    AuditCategory.User, AuditAction.Create, AuditOutcome.Denied, reasonCode,
                    userName, userName,
                    Details: "User creation validation failed."), cancellationToken);

                return result;
            }

            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategory.User, AuditAction.Create, AuditOutcome.Succeeded, AuditReasonCode.Succeeded,
                result.UserId, userName,
                Details: $"Created user '{userName}'"), cancellationToken);

            return result;
        }
        catch (Exception ex)
        {
            await _auditWriter.WriteAsync(new AdminAuditEvent(
                AuditCategory.User, AuditAction.Create, AuditOutcome.Failed, AuditReasonCode.PersistenceFailure,
                userName, userName,
                Details: $"Unexpected error ({ex.GetType().Name})"), cancellationToken);
            throw;
        }
    }
}