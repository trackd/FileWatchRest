namespace FileWatchRest.Tests;

public class ExternalConfigurationValidatorTests {
    [Fact]
    public void ValidConfigurationPassesValidation() {
        var config = new ExternalConfiguration {
            Folders = [new ExternalConfiguration.WatchedFolderConfig { FolderPath = "C:\\temp" }],
            ApiEndpoint = "http://localhost:8080/api/files",
            ProcessedFolder = "processed",
            DiagnosticsUrlPrefix = "http://localhost:5005/",
            AllowedExtensions = [".txt"],
            Logging = new SimpleFileLoggerOptions { LogLevel = LogLevel.Information }
        };

        ValidationResult result = ExternalConfigurationValidator.Validate(config);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void MissingFoldersFailsValidation() {
        var config = new ExternalConfiguration {
            Folders = [],
            ApiEndpoint = "http://localhost:8080/api/files",
            ProcessedFolder = "processed",
            DiagnosticsUrlPrefix = "http://localhost:5005/",
            AllowedExtensions = [".txt"],
            Logging = new SimpleFileLoggerOptions { LogLevel = LogLevel.Information }
        };

        ValidationResult result = ExternalConfigurationValidator.Validate(config);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle();
        result.Errors[0].ErrorMessage.Should().Contain("Folders");
    }

    [Fact]
    public void InvalidApiEndpointFailsValidation() {
        var config = new ExternalConfiguration {
            Folders = [new ExternalConfiguration.WatchedFolderConfig { FolderPath = "C:\\temp" }],
            ApiEndpoint = "not-a-valid-uri",
            ProcessedFolder = "processed",
            DiagnosticsUrlPrefix = "http://localhost:5005/",
            AllowedExtensions = [".txt"],
            Logging = new SimpleFileLoggerOptions { LogLevel = LogLevel.Information }
        };

        ValidationResult result = ExternalConfigurationValidator.Validate(config);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle();
        result.Errors[0].ErrorMessage.Should().Contain("ApiEndpoint");
    }
}
