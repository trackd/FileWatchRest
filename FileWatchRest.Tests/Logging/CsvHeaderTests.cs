namespace FileWatchRest.Tests;

public class CsvHeaderTests {
    [Fact]
    public void EnsureCsvHeaderMatchesReplacesOldHeader() {
        string expected = "Timestamp,Level,Message,Category,Exception,StatusCode";
        string temp = Path.Combine(Path.GetTempPath(), $"fwrest_csv_{Guid.NewGuid():N}.csv");
        try {
            File.WriteAllText(temp, "OLD_HEADER\nline1\nline2\n");

            SimpleFileLoggerProvider.EnsureCsvHeaderMatches(temp, expected);

            using var sr = new StreamReader(temp);
            string? first = sr.ReadLine();
            Assert.Equal(expected, first);
        }
        finally {
            try { if (File.Exists(temp)) { File.Delete(temp); } } catch { }
        }
    }
}
