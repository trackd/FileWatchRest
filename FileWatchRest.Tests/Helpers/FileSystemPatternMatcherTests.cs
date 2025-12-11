namespace FileWatchRest.Tests.Helpers;

public class FileSystemPatternMatcherTests {
    [Theory]
    [InlineData("file.txt", "*.txt", true)]
    [InlineData("README.md", "*.txt", false)]
    [InlineData("data1.csv", "data?.csv", true)]
    [InlineData("data10.csv", "data?.csv", false)]
    public void IsMatch_WildcardsBehave(string input, string pattern, bool expected) => Assert.Equal(expected, FileSystemPatternMatcher.IsMatch(input, pattern));

    [Fact]
    public void TryMatchAny_ReturnsMatchingPatternOrNull() {
        string[] patterns = ["*.log", "*.txt"];
        Assert.Equal("*.txt", FileSystemPatternMatcher.TryMatchAny("notes.txt", patterns));
        Assert.Null(FileSystemPatternMatcher.TryMatchAny("other.bin", patterns));
    }

    [Fact]
    public void ContainsWildcards_Detects() {
        Assert.True(FileSystemPatternMatcher.ContainsWildcards("*.txt"));
        Assert.False(FileSystemPatternMatcher.ContainsWildcards("readme.txt"));
    }

    [Fact]
    public void IsLiteralMatch_CaseInsensitive() => Assert.True(FileSystemPatternMatcher.IsLiteralMatch("ReadMe.TXT", "readme.txt"));
}
