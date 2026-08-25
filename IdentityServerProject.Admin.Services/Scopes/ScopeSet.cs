using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace IdentityServerProject.Services.Scopes;

/// <summary>
/// A distinct, ordered collection of scope names used at service boundaries.
/// </summary>
public sealed class ScopeSet : IReadOnlyCollection<ScopeName>
{
    private readonly ScopeName[] _scopes;

    public static ScopeSet Empty { get; } = new(Array.Empty<ScopeName>());

    public ScopeSet(IEnumerable<ScopeName> scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);

        _scopes = scopes
            .Where(scope => !scope.IsEmpty)
            .Distinct()
            .OrderBy(scope => scope)
            .ToArray();
    }

    public int Count => _scopes.Length;

    public bool Contains(ScopeName scope) => Array.BinarySearch(_scopes, scope) >= 0;

    public IReadOnlyList<string> ToValues() => _scopes.Select(scope => scope.Value).ToArray();

    public IEnumerator<ScopeName> GetEnumerator() => ((IEnumerable<ScopeName>)_scopes).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public static ScopeSet FromStrings(IEnumerable<string>? scopes) =>
        scopes is null ? Empty : new ScopeSet(scopes.Select(ScopeName.Create));
}
