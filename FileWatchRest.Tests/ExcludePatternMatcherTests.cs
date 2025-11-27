
using FileWatchRest.Helpers;

namespace FileWatchRest.Tests;
/// <summary>
/// Tests for exclude pattern matching using the framework-based FileSystemPatternMatcher.
/// </summary>
public class ExcludePatternMatcherTests {
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
    public void ExcludePatternMatcherMatchesCorrectly(string fileName, string[] patterns, bool expected) {
        // Arrange & Act
        bool excluded = FileSystemPatternMatcher.MatchesAny(fileName, patterns);

        // Assert
        Assert.Equal(expected, excluded);
    }

    [Fact]
    public void FileSystemPatternMatcherCaseInsensitiveMatches() {
        Assert.True(FileSystemPatternMatcher.IsMatch("SAP_file.txt", "SAP_*"));
        Assert.True(FileSystemPatternMatcher.IsMatch("sap_file.txt", "SAP_*"));
        Assert.True(FileSystemPatternMatcher.IsMatch("SaP_FiLe.TxT", "SAP_*"));
    }

    [Fact]
    public void FileSystemPatternMatcherSpecialCharsNotRegex() {
        Assert.True(FileSystemPatternMatcher.IsMatch("file.txt", "file.txt"));
        Assert.False(FileSystemPatternMatcher.IsMatch("fileXtxt", "file.txt")); // Dot is literal, not regex wildcard
    }

    [Fact]
    public void FileSystemPatternMatcherWildcardStarMatches() {
        Assert.True(FileSystemPatternMatcher.IsMatch("debug.log", "*.log"));
        Assert.True(FileSystemPatternMatcher.IsMatch("app.log", "*.log"));
        Assert.False(FileSystemPatternMatcher.IsMatch("debug.txt", "*.log"));
    }

    [Theory]
    [InlineData("*", true)]
    [InlineData("?", true)]
    [InlineData("literal", false)]
    [InlineData("", false)]
    public void ContainsWildcardsDetectsCorrectly(string pattern, bool expected) => Assert.Equal(expected, FileSystemPatternMatcher.ContainsWildcards(pattern));
}
