namespace IdentityServerProject.Services.IdentityResources;

/// <summary>
/// Why an identity resource mutation did or did not happen.
/// </summary>
public enum IdentityResourceEditOutcome
{
    /// <summary>The mutation was applied (or was already satisfied).</summary>
    Success,

    /// <summary>No identity resource of that name exists.</summary>
    NotFound,

    /// <summary>
    /// The resource or claim is protected. Distinct from <see cref="NotFound"/> on purpose:
    /// reporting a protected resource as missing tells the operator the wrong thing, and hides
    /// a deliberate guard behind what looks like a broken link.
    /// </summary>
    Protected,
}

/// <summary>
/// The outcome of a single identity resource mutation, carrying an operator-facing reason when
/// the mutation was refused.
/// </summary>
public sealed class IdentityResourceEditResult
{
    private IdentityResourceEditResult(IdentityResourceEditOutcome outcome, string? errorMessage)
    {
        Outcome = outcome;
        ErrorMessage = errorMessage;
    }

    public IdentityResourceEditOutcome Outcome { get; }

    /// <summary>Set only when <see cref="Outcome"/> is <see cref="IdentityResourceEditOutcome.Protected"/>.</summary>
    public string? ErrorMessage { get; }

    public bool Success => Outcome == IdentityResourceEditOutcome.Success;

    public static IdentityResourceEditResult Succeeded { get; } =
        new(IdentityResourceEditOutcome.Success, null);

    public static IdentityResourceEditResult NotFound { get; } =
        new(IdentityResourceEditOutcome.NotFound, null);

    public static IdentityResourceEditResult Protected(string errorMessage) =>
        new(IdentityResourceEditOutcome.Protected, errorMessage);
}
