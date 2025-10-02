namespace FileWatchRest.Tests;

public class ExternalConfigurationValidatorTests
{
    [Fact]
    public void ValidConfiguration_PassesValidation()
    {
        var config = new ExternalConfiguration
        {
            Folders = new[] { "C:\\temp" },
            ApiEndpoint = "http://localhost:8080/api/files",
            ProcessedFolder = "processed",
            DiagnosticsUrlPrefix = "http://localhost:5005/",
            AllowedExtensions = new[] { ".txt" },
            Logging = new LoggingOptions { LogLevel = "Information" }
        };

        var result = ExternalConfigurationValidator.Validate(config);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void MissingFolders_FailsValidation()
    {
        var config = new ExternalConfiguration
        {
            Folders = [],
            ApiEndpoint = "http://localhost:8080/api/files",
            ProcessedFolder = "processed",
            DiagnosticsUrlPrefix = "http://localhost:5005/",
            AllowedExtensions = new[] { ".txt" },
            Logging = new LoggingOptions { LogLevel = "Information" }
        };

        var result = ExternalConfigurationValidator.Validate(config);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle();
        result.Errors[0].ErrorMessage.Should().Contain("Folders");
    }

    [Fact]
    public void InvalidApiEndpoint_FailsValidation()
    {
        var config = new ExternalConfiguration
        {
            Folders = new[] { "C:\\temp" },
            ApiEndpoint = "not-a-valid-uri",
            ProcessedFolder = "processed",
            DiagnosticsUrlPrefix = "http://localhost:5005/",
            AllowedExtensions = new[] { ".txt" },
            Logging = new LoggingOptions { LogLevel = "Information" }
        };

        var result = ExternalConfigurationValidator.Validate(config);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle();
        result.Errors[0].ErrorMessage.Should().Contain("ApiEndpoint");
    }
}
