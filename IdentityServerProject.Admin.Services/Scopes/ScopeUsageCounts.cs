using System;
using System.Collections.Generic;
using System.Linq;

namespace IdentityServerProject.Services.Scopes;

/// <summary>
/// Immutable client-reference counts indexed by a strongly typed scope name.
/// Missing scopes have no client references.
/// </summary>
public sealed class ScopeUsageCounts
{
    private readonly IReadOnlyDictionary<ScopeName, int> _counts;

    public ScopeUsageCounts(IEnumerable<KeyValuePair<ScopeName, int>> counts)
    {
        ArgumentNullException.ThrowIfNull(counts);
        _counts = counts.ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    public int this[ScopeName scope] => _counts.TryGetValue(scope, out var count) ? count : 0;
}
