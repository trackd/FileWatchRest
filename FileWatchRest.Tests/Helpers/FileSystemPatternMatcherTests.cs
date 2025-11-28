using FileWatchRest.Helpers;
using FluentAssertions;
using Xunit;

namespace FileWatchRest.Tests.Helpers;

public class FileSystemPatternMatcherTests {
    [Theory]
    [InlineData("file.txt", "*.txt", true)]
    [InlineData("README.md", "*.txt", false)]
    [InlineData("data1.csv", "data?.csv", true)]
    [InlineData("data10.csv", "data?.csv", false)]
    public void IsMatch_WildcardsBehave(string input, string pattern, bool expected) => FileSystemPatternMatcher.IsMatch(input, pattern).Should().Be(expected);

    [Fact]
    public void TryMatchAny_ReturnsMatchingPatternOrNull() {
        string[] patterns = ["*.log", "*.txt"];
        FileSystemPatternMatcher.TryMatchAny("notes.txt", patterns).Should().Be("*.txt");
        FileSystemPatternMatcher.TryMatchAny("other.bin", patterns).Should().BeNull();
    }

    [Fact]
    public void ContainsWildcards_Detects() {
        FileSystemPatternMatcher.ContainsWildcards("*.txt").Should().BeTrue();
        FileSystemPatternMatcher.ContainsWildcards("readme.txt").Should().BeFalse();
    }

    [Fact]
    public void IsLiteralMatch_CaseInsensitive() => FileSystemPatternMatcher.IsLiteralMatch("ReadMe.TXT", "readme.txt").Should().BeTrue();
}
