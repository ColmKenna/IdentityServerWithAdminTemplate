namespace IdentityServerProject.Services.AuditLogs;

public static class AuditCategories
{
    public const string User = "User";
    public const string Client = "Client";
    public const string Grant = "Grant";
    public const string ApiResource = "ApiResource";
    public const string ApiScope = "ApiScope";
    public const string IdentityResource = "IdentityResource";
    public const string SecretReveal = "SecretReveal";
    public const string Role = "Role";
}

public static class AuditActions
{
    // Shared across categories
    public const string Create = "Create";
    public const string Update = "Update";
    public const string UpdateBasics = "UpdateBasics";
    public const string SetEnabled = "SetEnabled";
    public const string AddClaim = "AddClaim";
    public const string RemoveClaim = "RemoveClaim";
    public const string GenerateSecret = "GenerateSecret";
    public const string RevokeSecret = "RevokeSecret";
    public const string Delete = "Delete";

    // User
    public const string UpdatePassword = "UpdatePassword";
    public const string ResetPassword = "ResetPassword";
    public const string Unlock = "Unlock";
    public const string SuspendUser = "SuspendUser";
    public const string DeleteUser = "DeleteUser";
    public const string AddRole = "AddRole";
    public const string RemoveRole = "RemoveRole";
    public const string RevokeUserAccess = "RevokeUserAccess";
    public const string SendBackChannelLogout = "SendBackChannelLogout";
    public const string RemoveGrantsOnUserDelete = "RemoveGrantsOnUserDelete";

    // Client
    public const string UpdateAuthentication = "UpdateAuthentication";
    public const string UpdatePermissions = "UpdatePermissions";
    public const string UpdateTokenSettings = "UpdateTokenSettings";

    // Grant
    public const string Revoke = "Revoke";
    public const string BulkRevoke = "BulkRevoke";

    // ApiResource
    public const string AttachScope = "AttachScope";
    public const string CreateScope = "CreateScope";
    public const string DetachScope = "DetachScope";

    // SecretReveal (reserved for TASK-03)
    public const string Issue = "Issue";
    public const string Consume = "Consume";
}

public static class AuditReasonCodes
{
    public const string Succeeded = "Succeeded";
    public const string NotFound = "NotFound";
    public const string ValidationFailed = "ValidationFailed";
    public const string NameCollision = "NameCollision";
    public const string LastAdministrator = "LastAdministrator";
    public const string ClientEnabled = "ClientEnabled";
    public const string ProtectedResource = "ProtectedResource";
    public const string SecretRequired = "SecretRequired";
    public const string Expired = "Expired";
    public const string WrongContext = "WrongContext";
    public const string PersistenceFailure = "PersistenceFailure";
    public const string SelfDemotion = "SelfDemotion";
    public const string LastUsableSecret = "LastUsableSecret";
    public const string RetentionPeriod = "RetentionPeriod";
    public const string NotificationFailure = "NotificationFailure";
    public const string ReferencedResource = "ReferencedResource";

    // Project-specific additions not in the doc's example list
    public const string SelfAction = "SelfAction";
    public const string ReservedClaimType = "ReservedClaimType";
}
