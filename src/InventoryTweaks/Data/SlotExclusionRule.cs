using System;
using System.Text;
using System.Text.RegularExpressions;

namespace InventoryTweaks.Data;

/// <summary>
///     A single parsed slot exclusion entry of the form "PrefabPattern:SlotPattern".
///     Both sides support glob-style "*" wildcards, where "*" matches any run of characters
///     (including none) and may appear anywhere in the pattern (e.g. "Output*", "*Output", "*Out*").
///     Matching is case-insensitive. Patterns are compiled once on construction so that the
///     per-slot lookup performed during stow remains cheap.
/// </summary>
public sealed class SlotExclusionRule
{
    private readonly Func<string, bool> _prefabMatcher;
    private readonly Func<string, bool> _slotMatcher;

    private SlotExclusionRule(Func<string, bool> prefabMatcher, Func<string, bool> slotMatcher)
    {
        _prefabMatcher = prefabMatcher;
        _slotMatcher = slotMatcher;
    }

    /// <summary>
    ///     Creates a rule from the raw prefab and slot patterns of a single exclusion entry.
    /// </summary>
    /// <param name="prefabPattern">The prefab-name side of the entry (may contain "*" wildcards).</param>
    /// <param name="slotPattern">The slot-name side of the entry (may contain "*" wildcards).</param>
    /// <returns>A compiled rule that can be matched against runtime prefab and slot names.</returns>
    public static SlotExclusionRule Create(string prefabPattern, string slotPattern)
    {
        return new SlotExclusionRule(CompileMatcher(prefabPattern), CompileMatcher(slotPattern));
    }

    /// <summary>
    ///     Determines whether the supplied prefab name and slot name both satisfy this rule's patterns.
    /// </summary>
    /// <param name="prefabName">The runtime prefab name of the slot's parent.</param>
    /// <param name="slotName">The runtime slot name being tested.</param>
    /// <returns>True when both sides match, false otherwise.</returns>
    public bool Matches(string prefabName, string slotName)
    {
        return _prefabMatcher(prefabName ?? string.Empty) && _slotMatcher(slotName ?? string.Empty);
    }

    /// <summary>
    ///     Compiles a single glob pattern into a case-insensitive matching delegate, choosing the
    ///     cheapest representation for the pattern shape.
    /// </summary>
    /// <param name="pattern">The glob pattern to compile.</param>
    /// <returns>A delegate that returns true when its argument matches the pattern.</returns>
    private static Func<string, bool> CompileMatcher(string pattern)
    {
        pattern ??= string.Empty;

        // A pattern made up solely of wildcards (including a bare "*") matches anything.
        if (pattern.Length > 0 && pattern.Trim('*').Length == 0)
            return _ => true;

        // Fast path: no wildcard means a plain case-insensitive equality check, avoiding regex overhead.
        if (pattern.IndexOf('*') < 0)
            return value => string.Equals(value, pattern, StringComparison.OrdinalIgnoreCase);

        var regex = new Regex(GlobToRegexPattern(pattern),
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);
        return value => regex.IsMatch(value);
    }

    /// <summary>
    ///     Converts a glob pattern into an anchored regular expression, escaping every literal
    ///     character and translating "*" into ".*".
    /// </summary>
    /// <param name="glob">The glob pattern to convert.</param>
    /// <returns>An anchored regex pattern string equivalent to the glob.</returns>
    private static string GlobToRegexPattern(string glob)
    {
        var builder = new StringBuilder(glob.Length + 4);
        builder.Append('^');

        foreach (var ch in glob)
            builder.Append(ch == '*' ? ".*" : Regex.Escape(ch.ToString()));

        builder.Append('$');
        return builder.ToString();
    }
}