namespace FileWatchRest.Tests;

public class ExternalConfigurationValidatorEdgeCasesTests
{
    [Fact]
    public void EmptyApiEndpoint_FailsValidation()
    {
        var config = new ExternalConfiguration
        {
            Folders = new[] { "C:\\temp" },
            ApiEndpoint = string.Empty,
            ProcessedFolder = "processed",
            DiagnosticsUrlPrefix = "http://localhost:5005/",
            AllowedExtensions = new[] { ".txt" },
            Logging = new LoggingOptions { LogLevel = "Information" }
        };

        var v = new ExternalConfigurationValidator();
        var r = v.Validate(config);
        r.IsValid.Should().BeFalse();
        r.Errors.Should().ContainSingle(e => e.PropertyName.Contains("ApiEndpoint"));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    public void NegativeNumericValues_FailValidation(int badValue)
    {
        var config = new ExternalConfiguration
        {
            Folders = new[] { "C:\\temp" },
            ApiEndpoint = "http://localhost:8080/api/files",
            ProcessedFolder = "processed",
            DiagnosticsUrlPrefix = "http://localhost:5005/",
            AllowedExtensions = new[] { ".txt" },
            Logging = new LoggingOptions { LogLevel = "Information" },
            DebounceMilliseconds = badValue
        };

        var v = new ExternalConfigurationValidator();
        var r = v.Validate(config);
        r.IsValid.Should().BeFalse();
        r.Errors.Should().ContainSingle(e => e.PropertyName.Contains("DebounceMilliseconds"));
    }

    [Fact]
    public void AllowedExtensions_BadFormat_FailsValidation()
    {
        var config = new ExternalConfiguration
        {
            Folders = new[] { "C:\\temp" },
            ApiEndpoint = "http://localhost:8080/api/files",
            ProcessedFolder = "processed",
            DiagnosticsUrlPrefix = "http://localhost:5005/",
            AllowedExtensions = new[] { "txt", "" },
            Logging = new LoggingOptions { LogLevel = "Information" }
        };

        var v = new ExternalConfigurationValidator();
        var r = v.Validate(config);
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.PropertyName.Contains("AllowedExtensions"));
    }
}
