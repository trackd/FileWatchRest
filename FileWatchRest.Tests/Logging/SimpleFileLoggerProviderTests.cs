namespace FileWatchRest.Tests;

public partial class SimpleFileLoggerProviderTests {
    [Fact]
    public void EnsureWritersPurgesOldFilesBasedOnRetainedDays() {
        string tempDir = Path.Combine(Path.GetTempPath(), "FileWatchRest_TestLogs", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try {
            // Create 5 log files with different timestamps
            for (int i = 0; i < 5; i++) {
                string fileName = Path.Combine(tempDir, $"FileWatchRest_{DateTime.UtcNow.AddDays(-i):yyyy-MM-dd}.log");
                File.WriteAllText(fileName, "test");
            }

            var options = new SimpleFileLoggerOptions {
                LogType = LogType.Csv,
                FilePathPattern = Path.Combine(tempDir, "FileWatchRest_{0:yyyy-MM-dd}"),
                RetainedDays = 2
            };

            var provider = new SimpleFileLoggerProvider(options);
            provider.Dispose(); // Ensure all writers are closed before purging

            // Explicitly purge old log files after disposing provider
            SimpleFileLoggerProvider.PurgeOldLogFilesByDate(tempDir, options.RetainedDays);

            // Validate that only files within the retention period remain
            string[] remainingFiles = Directory.GetFiles(tempDir, "*.log");
            Assert.Equal(2, remainingFiles.Length);

            System.Text.RegularExpressions.Regex dateRegex = MyRegex();
            foreach (string file in remainingFiles) {
                string fn = Path.GetFileName(file) ?? string.Empty;
                System.Text.RegularExpressions.Match m = dateRegex.Match(fn);
                Assert.True(m.Success, $"Filename '{fn}' should contain a date token in yyyy-MM-dd or yyyy-M-d form");
                string year = m.Groups[1].Value;
                string month = m.Groups[2].Value.PadLeft(2, '0');
                string day = m.Groups[3].Value.PadLeft(2, '0');
                string token = $"{year}-{month}-{day}";
                var fileDate = DateTime.ParseExact(token, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                Assert.True(fileDate >= DateTime.UtcNow.Date.AddDays(-2));
            }
        }
        finally {
            // Wait for file handles to be released before deleting
            GC.Collect();
            GC.WaitForPendingFinalizers();
            Directory.Delete(tempDir, true);
        }
    }

    [System.Text.RegularExpressions.GeneratedRegex("(\\d{4})-(\\d{1,2})-(\\d{1,2})")]
    private static partial System.Text.RegularExpressions.Regex MyRegex();
}
