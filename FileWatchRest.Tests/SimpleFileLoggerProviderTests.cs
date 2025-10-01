namespace FileWatchRest.Tests;

public class SimpleFileLoggerProviderTests
{
    [Fact]
    public void EnsureWriters_PurgesOldFiles_BasedOn_RetainedFileCountLimit()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "FileWatchRest_TestLogs", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            // Create 5 CSV files with prefix
            for (int i = 0; i < 5; i++)
            {
                var fn = Path.Combine(tempDir, $"FileWatchRest_{i:000000}.csv");
                File.WriteAllText(fn, "test");
                File.SetLastWriteTimeUtc(fn, DateTime.UtcNow.AddMinutes(-i));
            }

            var options = new SimpleFileLoggerOptions
            {
                LogType = LogType.Csv,
                FilePathPattern = Path.Combine(tempDir, "FileWatchRest_{0:yyyyMMdd_HHmmss}"),
                RetainedFileCountLimit = 2
            };

            var provider = new SimpleFileLoggerProvider(options);

            // After construction, EnsureWriters should have purged old files so only RetainedFileCountLimit historical files remain.
            var remaining = Directory.GetFiles(tempDir, "FileWatchRest*.csv");
            remaining.Length.Should().BeLessOrEqualTo(options.RetainedFileCountLimit + 1);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }
}
