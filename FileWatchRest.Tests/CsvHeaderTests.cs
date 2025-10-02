namespace FileWatchRest.Tests;

public class CsvHeaderTests
{
    [Fact]
    public void EnsureCsvHeaderMatches_ReplacesOldHeader()
    {
        var expected = "Timestamp,Level,Message,Category,Exception,StatusCode";
        var temp = Path.Combine(Path.GetTempPath(), $"fwrest_csv_{Guid.NewGuid():N}.csv");
        try
        {
            File.WriteAllText(temp, "OLD_HEADER\nline1\nline2\n");

            // Call the internal helper (InternalsVisibleTo allows this)
            SimpleFileLoggerProvider.EnsureCsvHeaderMatches(temp, expected);

            using var sr = new StreamReader(temp);
            var first = sr.ReadLine();
            first.Should().Be(expected);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }
}
