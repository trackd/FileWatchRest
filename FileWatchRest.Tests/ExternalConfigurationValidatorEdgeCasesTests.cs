namespace FileWatchRest.Tests;

public class ExternalConfigurationValidatorEdgeCasesTests {
    [Fact]
    public void EmptyApiEndpointFailsValidation() {
        var config = new ExternalConfiguration {
            Folders = [new ExternalConfiguration.WatchedFolderConfig { FolderPath = "C:\\temp" }],
            ApiEndpoint = string.Empty,
            ProcessedFolder = "processed",
            DiagnosticsUrlPrefix = "http://localhost:5005/",
            AllowedExtensions = [".txt"],
            Logging = new SimpleFileLoggerOptions { LogLevel = LogLevel.Information }
        };

        ValidationResult r = ExternalConfigurationValidator.Validate(config);
        r.IsValid.Should().BeFalse();
        r.Errors.Should().ContainSingle(e => e.PropertyName.Contains("ApiEndpoint"));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    public void NegativeNumericValuesFailValidation(int badValue) {
        var config = new ExternalConfiguration {
            Folders = [new ExternalConfiguration.WatchedFolderConfig { FolderPath = "C:\\temp" }],
            ApiEndpoint = "http://localhost:8080/api/files",
            ProcessedFolder = "processed",
            DiagnosticsUrlPrefix = "http://localhost:5005/",
            AllowedExtensions = [".txt"],
            Logging = new SimpleFileLoggerOptions { LogLevel = LogLevel.Information },
            DebounceMilliseconds = badValue
        };

        ValidationResult r = ExternalConfigurationValidator.Validate(config);
        r.IsValid.Should().BeFalse();
        r.Errors.Should().ContainSingle(e => e.PropertyName.Contains("DebounceMilliseconds"));
    }

    [Fact]
    public void AllowedExtensionsBadFormatFailsValidation() {
        var config = new ExternalConfiguration {
            Folders = [new ExternalConfiguration.WatchedFolderConfig { FolderPath = "C:\\temp" }],
            ApiEndpoint = "http://localhost:8080/api/files",
            ProcessedFolder = "processed",
            DiagnosticsUrlPrefix = "http://localhost:5005/",
            AllowedExtensions = ["txt", ""],
            Logging = new SimpleFileLoggerOptions { LogLevel = LogLevel.Information }
        };

        ValidationResult r = ExternalConfigurationValidator.Validate(config);
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.PropertyName.Contains("AllowedExtensions"));
    }
}
