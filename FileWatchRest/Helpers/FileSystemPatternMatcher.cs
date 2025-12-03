namespace FileWatchRest.Helpers;

/// <summary>
/// Framework-based wildcard pattern matcher using System.IO.Enumeration.FileSystemName.
/// This class uses the built-in .NET wildcard matching which supports * and ? wildcards.
/// </summary>
internal static class FileSystemPatternMatcher {
    /// <summary>
    /// Tests if the input matches the wildcard pattern (case-insensitive).
    /// Uses the framework's FileSystemName.MatchesSimpleExpression which supports:
    /// - * (matches zero or more characters)
    /// - ? (matches exactly one character)
    /// </summary>
    /// <param name="input"></param>
    /// <param name="pattern"></param>
    public static bool IsMatch(string input, string pattern) {
        return !string.IsNullOrEmpty(input) && !string.IsNullOrEmpty(pattern) && FileSystemName.MatchesSimpleExpression(
            pattern,
            input,
            ignoreCase: true);
    }

    /// <summary>
    /// Tests if input matches any of the given patterns and returns the matching pattern.
    /// Returns null if no match found.
    /// </summary>
    /// <param name="input"></param>
    /// <param name="patterns"></param>
    public static string? TryMatchAny(string input, ReadOnlySpan<string> patterns) {
        if (string.IsNullOrEmpty(input)) {
            return null;
        }

        foreach (string pattern in patterns) {
            if (string.IsNullOrEmpty(pattern)) {
                continue;
            }

            if (FileSystemName.MatchesSimpleExpression(
                pattern,
                input,
                ignoreCase: true)) {
                return pattern;
            }
        }

        return null;
    }

    /// <summary>
    /// Tests if input matches any of the given patterns.
    /// </summary>
    /// <param name="input"></param>
    /// <param name="patterns"></param>
    public static bool MatchesAny(string input, ReadOnlySpan<string> patterns) => TryMatchAny(input, patterns) is not null;

    /// <summary>
    /// Checks if a pattern contains wildcard characters.
    /// </summary>
    /// <param name="pattern"></param>
    public static bool ContainsWildcards(string pattern) => !string.IsNullOrEmpty(pattern) && (pattern.Contains('*') || pattern.Contains('?'));

    /// <summary>
    /// Fast path: check for literal match when no wildcards are present.
    /// </summary>
    /// <param name="pattern"></param>
    /// <param name="input"></param>
    public static bool IsLiteralMatch(string pattern, string input) => string.Equals(pattern, input, StringComparison.OrdinalIgnoreCase);
}
