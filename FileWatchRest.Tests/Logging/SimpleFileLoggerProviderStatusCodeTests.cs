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
                FilePathPattern = csvPath,
            };

            var provider = new SimpleFileLoggerProvider(options);
            ILogger logger = provider.CreateLogger("UnitTest");

            if (logger.IsEnabled(LogLevel.Information)) {
                LoggerDelegates.UploadResult(logger, "file.txt", 201, null);
            }

            provider.Dispose();

            string allText = File.ReadAllText(csvPath);

            allText.Should().Contain("StatusCode");
            allText.Should().Contain(",201");
        }
        finally {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }
}
