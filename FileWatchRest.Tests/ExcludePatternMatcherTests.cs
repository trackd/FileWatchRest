namespace FileWatchRest.Tests;

public class ExcludePatternMatcherTests
{
    [Theory]
    [InlineData("SAP_file.txt", new[] { "SAP_*" }, true)]
    [InlineData("file_SAP.txt", new[] { "SAP_*" }, false)]
    [InlineData("Backup_2025.bak", new[] { "Backup_*" }, true)]
    [InlineData("report_temp", new[] { "*_temp" }, true)]
    [InlineData("temp_report", new[] { "*_temp" }, false)]
    [InlineData("data.bak", new[] { "*.bak" }, true)]
    [InlineData("data.txt", new[] { "*.bak" }, false)]
    [InlineData("log1.txt", new[] { "log?.txt" }, true)]
    [InlineData("log12.txt", new[] { "log?.txt" }, false)]
    [InlineData("file.txt", new[] { "*" }, true)]
    [InlineData("file.txt", new string[0], false)]
    [InlineData("file.txt", new[] { "SAP_*", "*.txt" }, true)]
    [InlineData("file.csv", new[] { "SAP_*", "*.txt" }, false)]
    [InlineData("SAP_HelloWorld.log", new[] { "SAP_*.log" }, true)]
    public void ExcludePatternMatcher_Matches_Correctly(string fileName, string[] patterns, bool expected)
    {
        // Arrange & Act
        var excluded = WildcardPatternCache.MatchesAny(fileName, patterns);

        // Assert
        Assert.Equal(expected, excluded);
    }

    [Fact]
    public void WildcardPatternMatcher_CaseInsensitive_Matches()
    {
        var matcher = new WildcardPatternMatcher("SAP_*");

        Assert.True(matcher.IsMatch("SAP_file.txt"));
        Assert.True(matcher.IsMatch("sap_file.txt"));
        Assert.True(matcher.IsMatch("SaP_FiLe.TxT"));
    }

    [Fact]
    public void WildcardPatternMatcher_SpecialRegexChars_Escaped()
    {
        var matcher = new WildcardPatternMatcher("file.txt");

        Assert.True(matcher.IsMatch("file.txt"));
        Assert.False(matcher.IsMatch("fileXtxt")); // Dot should not act as regex wildcard
    }

    [Fact]
    public void WildcardPatternMatcher_Span_Matches()
    {
        var matcher = new WildcardPatternMatcher("*.log");
        ReadOnlySpan<char> fileName = "debug.log".AsSpan();

        Assert.True(matcher.IsMatch(fileName));
    }

    [Fact]
    public void WildcardPatternCache_Caches_Patterns()
    {
        var pattern = "test_*";
        var matcher1 = WildcardPatternCache.GetOrCreate(pattern);
        var matcher2 = WildcardPatternCache.GetOrCreate(pattern);

        Assert.Same(matcher1, matcher2);
    }

    [Theory]
    [InlineData("*", true)]
    [InlineData("?", true)]
    [InlineData("[abc]", true)]
    [InlineData("literal", false)]
    public void ContainsWildcards_DetectsCorrectly(string pattern, bool expected)
    {
        Assert.Equal(expected, WildcardPatternMatcher.ContainsWildcards(pattern));
    }
}
