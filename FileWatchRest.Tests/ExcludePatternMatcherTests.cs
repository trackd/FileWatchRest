using System.Collections.Generic;
using Xunit;
using FileWatchRest.Services;

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
    public void ExcludePatternMatcher_Matches_Correctly(string fileName, string[] patterns, bool expected)
    {
        // Arrange
        var patternList = patterns == null ? null : new List<string>(patterns);
        // Act
        bool result = Worker.MatchesExcludePattern(fileName, patternList);
        // Assert
        Assert.Equal(expected, result);
    }
}
