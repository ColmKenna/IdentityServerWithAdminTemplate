namespace IdentityServerProject.Services.IdentityResources;

public class IdentityResourceEditorModel
{
    public string Name { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public bool Enabled { get; set; }
    public bool Required { get; set; }
    public bool Emphasize { get; set; }
    public bool ShowInDiscoveryDocument { get; set; }
    public List<string> UserClaims { get; set; } = new();

    /// <summary>
    ///     True when every mutation on this resource is refused (see
    ///     <see cref="BuiltInIdentityResourcePolicy" />). The editor renders read-only in that case, so
    ///     the operator is told before submitting rather than after.
    /// </summary>
    public bool IsProtected { get; set; }
}