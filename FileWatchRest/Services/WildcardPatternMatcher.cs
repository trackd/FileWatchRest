namespace FileWatchRest.Services;

/// <summary>
/// High-performance wildcard pattern matcher inspired by PowerShell's implementation.
/// Supports * (zero or more characters) and ? (single character) wildcards.
/// Uses precompiled regex with source generation for optimal performance.
/// </summary>
internal sealed partial class WildcardPatternMatcher
{
    private readonly Regex _compiledPattern;
    private readonly string _originalPattern;

    public WildcardPatternMatcher(string pattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern, nameof(pattern));

        _originalPattern = pattern;
        _compiledPattern = CreateRegex(ConvertWildcardToRegex(pattern));
    }

    /// <summary>
    /// Tests if the input matches the wildcard pattern (case-insensitive).
    /// </summary>
    public bool IsMatch(ReadOnlySpan<char> input)
    {
        // Span-based matching for performance
        return _compiledPattern.IsMatch(input);
    }

    /// <summary>
    /// Tests if the input matches the wildcard pattern (case-insensitive).
    /// </summary>
    public bool IsMatch(string input)
    {
        return _compiledPattern.IsMatch(input);
    }

    /// <summary>
    /// Converts a wildcard pattern to a regex pattern.
    /// Based on PowerShell's WildcardPattern implementation.
    /// </summary>
    private static string ConvertWildcardToRegex(string pattern)
    {
        var sb = new StringBuilder(pattern.Length * 2);
        sb.Append('^'); // Anchor at start

        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];
            switch (c)
            {
                case '*':
                    // Match zero or more characters (non-greedy when possible)
                    sb.Append(".*");
                    break;

                case '?':
                    // Match exactly one character
                    sb.Append('.');
                    break;

                case '[':
                    // Character class - pass through but validate
                    int closeBracket = pattern.IndexOf(']', i + 1);
                    if (closeBracket > i)
                    {
                        // Valid character class, include it
                        sb.Append(pattern.AsSpan(i, closeBracket - i + 1));
                        i = closeBracket;
                    }
                    else
                    {
                        // Unclosed bracket, treat as literal
                        sb.Append("\\[");
                    }
                    break;

                case ']':
                    // Closing bracket without opening, treat as literal
                    sb.Append("\\]");
                    break;

                // Escape regex special characters
                case '\\':
                case '^':
                case '$':
                case '.':
                case '|':
                case '+':
                case '(':
                case ')':
                case '{':
                case '}':
                    sb.Append('\\').Append(c);
                    break;

                default:
                    sb.Append(c);
                    break;
            }
        }

        sb.Append('$'); // Anchor at end
        return sb.ToString();
    }

    private static Regex CreateRegex(string regexPattern)
    {
        // Use compiled regex with case-insensitive, culture-invariant matching
        return new Regex(
            regexPattern,
            RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100)); // Timeout to prevent ReDoS
    }

    /// <summary>
    /// Checks if a pattern contains wildcard characters.
    /// </summary>
    public static bool ContainsWildcards(ReadOnlySpan<char> pattern)
    {
        foreach (var c in pattern)
        {
            if (c == '*' || c == '?' || c == '[' || c == ']')
                return true;
        }
        return false;
    }

    /// <summary>
    /// Fast path: check for literal match when no wildcards are present.
    /// </summary>
    public static bool IsLiteralMatch(string pattern, string input)
    {
        return string.Equals(pattern, input, StringComparison.OrdinalIgnoreCase);
    }

    public override string ToString() => _originalPattern;
}

/// <summary>
/// Factory for creating and caching wildcard pattern matchers.
/// </summary>
internal static class WildcardPatternCache
{
    private static readonly ConcurrentDictionary<string, WildcardPatternMatcher> _cache = new(StringComparer.OrdinalIgnoreCase);
    private const int MaxCacheSize = 100; // Prevent unbounded growth

    /// <summary>
    /// Gets or creates a cached pattern matcher.
    /// </summary>
    public static WildcardPatternMatcher GetOrCreate(string pattern)
    {
        // Fast path for literal patterns (no wildcards)
        if (!WildcardPatternMatcher.ContainsWildcards(pattern))
        {
            // For literals, we can use a simple matcher without regex compilation
            return new WildcardPatternMatcher(pattern);
        }

        // Check cache first
        if (_cache.TryGetValue(pattern, out var cached))
        {
            return cached;
        }

        // Limit cache size to prevent memory leaks
        if (_cache.Count >= MaxCacheSize)
        {
            _cache.Clear(); // Simple eviction strategy
        }

        return _cache.GetOrAdd(pattern, p => new WildcardPatternMatcher(p));
    }

    /// <summary>
    /// Tests if input matches any of the given patterns.
    /// Optimized for common case of checking multiple patterns.
    /// </summary>
    public static bool MatchesAny(string input, ReadOnlySpan<string> patterns)
    {
        foreach (var pattern in patterns)
        {
            // Fast path: literal comparison when no wildcards
            if (!WildcardPatternMatcher.ContainsWildcards(pattern))
            {
                if (WildcardPatternMatcher.IsLiteralMatch(pattern, input))
                {
                    return true;
                }
                continue;
            }

            // Use cached pattern matcher
            var matcher = GetOrCreate(pattern);
            if (matcher.IsMatch(input))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Tests if input matches any of the given patterns and returns the matching pattern.
    /// Returns null if no match found.
    /// </summary>
    public static string? TryMatchAny(string input, ReadOnlySpan<string> patterns)
    {
        foreach (var pattern in patterns)
        {
            // Fast path: literal comparison when no wildcards
            if (!WildcardPatternMatcher.ContainsWildcards(pattern))
            {
                if (WildcardPatternMatcher.IsLiteralMatch(pattern, input))
                {
                    return pattern;
                }
                continue;
            }

            // Use cached pattern matcher
            var matcher = GetOrCreate(pattern);
            if (matcher.IsMatch(input))
            {
                return pattern;
            }
        }

        return null;
    }
}
