namespace IdentityServerProject.Services.Apis;

/// <summary>
///     Outcome of a write operation against an API resource, distinguishing "not found"
///     from validation-style rejections so callers can render an appropriate response.
/// </summary>
public enum ApiResourceSaveResult
{
    Success,
    NotFound,
    NameCollision
}