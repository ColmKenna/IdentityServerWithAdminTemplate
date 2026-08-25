using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace IdentityServerProject.Services.Validation;

/// <summary>
/// A collection of validation error messages keyed by member or field name,
/// providing fluent methods to accumulate errors across multiple validation checks.
/// </summary>
public sealed class ValidationErrorDictionary : IReadOnlyDictionary<string, string[]>
{
    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.Ordinal);

    public ValidationErrorDictionary()
    {
    }

    public static ValidationErrorDictionary Empty => new();

    public bool HasErrors => _errors.Count > 0;
    public bool IsEmpty => _errors.Count == 0;
    public int Count => _errors.Count;

    public ValidationErrorDictionary AddError(string field, string message)
    {
        var key = field ?? string.Empty;
        if (!_errors.TryGetValue(key, out var list))
        {
            list = new List<string>();
            _errors[key] = list;
        }

        list.Add(message);
        return this;
    }

    public ValidationErrorDictionary AddErrors(string field, IEnumerable<string> messages)
    {
        var key = field ?? string.Empty;
        if (!_errors.TryGetValue(key, out var list))
        {
            list = new List<string>();
            _errors[key] = list;
        }

        list.AddRange(messages);
        return this;
    }

    public IReadOnlyDictionary<string, string[]> ToDictionary() =>
        _errors.ToDictionary(k => k.Key, v => v.Value.ToArray(), StringComparer.Ordinal);

    public string[] this[string key] => _errors[key].ToArray();

    public IEnumerable<string> Keys => _errors.Keys;

    public IEnumerable<string[]> Values => _errors.Values.Select(v => v.ToArray());

    public bool ContainsKey(string key) => _errors.ContainsKey(key);

    public bool TryGetValue(string key, out string[] value)
    {
        if (_errors.TryGetValue(key, out var list))
        {
            value = list.ToArray();
            return true;
        }

        value = Array.Empty<string>();
        return false;
    }

    public IEnumerator<KeyValuePair<string, string[]>> GetEnumerator() =>
        _errors.Select(kvp => new KeyValuePair<string, string[]>(kvp.Key, kvp.Value.ToArray())).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
