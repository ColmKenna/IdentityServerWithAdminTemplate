namespace IdentityServerProject.Services.AuditLogs;

/// <summary>
/// Marker for deliberately projected, allow-listed audit value DTOs. Request models,
/// EF entities, and other arbitrary objects are redacted by <see cref="AuditWriter"/>.
/// </summary>
public interface IAuditValue;
