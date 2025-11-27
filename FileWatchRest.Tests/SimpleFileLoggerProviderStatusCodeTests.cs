namespace FileWatchRest.Tests;

public class SimpleFileLoggerProviderStatusCodeTests {
    [Fact]
    public void LoggerCapturesStatusCodeAndCsvContainsStatusColumn() {
        string tempDir = Path.Combine(Path.GetTempPath(), "FileWatchRest_TestLogs", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        string csvPath = Path.Combine(tempDir, "test.csv");

        try {
            var options = new SimpleFileLoggerOptions {
                LogType = LogType.Csv,
                FilePathPattern = csvPath, // allow full path (with extension)
                // RetainedFileCountLimit removed; retention is now by days
            };

            var provider = new SimpleFileLoggerProvider(options);
            ILogger logger = provider.CreateLogger("UnitTest");

            // Log a structured message that includes StatusCode
            if (logger.IsEnabled(LogLevel.Information)) {
                LoggerDelegates.UploadResult(logger, "file.txt", 201, null);
            }

            // Ensure the provider flushes and closes the writer so the test can read the file
            provider.Dispose();

            // Ensure file exists and read contents
            string allText = File.ReadAllText(csvPath);

            // Header should include StatusCode
            allText.Should().Contain("StatusCode");
            // The logged line should contain the status code 201
            allText.Should().Contain(",201");
        }
        finally {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }
}
