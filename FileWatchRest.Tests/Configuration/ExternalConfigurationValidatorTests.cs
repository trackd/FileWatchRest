using System.Linq;
using FluentAssertions;
using Xunit;
using FileWatchRest.Configuration;

namespace FileWatchRest.Tests.Configuration
{
    public class ExternalConfigurationValidatorTests
    {
        [Fact]
        public void Validate_returns_errors_for_invalid_configuration()
        {
            var cfg = new ExternalConfiguration
            {
                Folders = null!,
                Actions = null!,
                ProcessedFolder = "",
                DebounceMilliseconds = -1,
                Retries = -1,
                RetryDelayMilliseconds = -1,
                WatcherMaxRestartAttempts = -1,
                WatcherRestartDelayMilliseconds = -1,
                DiagnosticsUrlPrefix = "not a uri",
                ChannelCapacity = 0,
                MaxParallelSends = 0,
                FileWatcherInternalBufferSize = 0,
                WaitForFileReadyMilliseconds = -1,
                MaxContentBytes = -1,
                StreamingThresholdBytes = -1,
                CircuitBreakerFailureThreshold = -1,
                CircuitBreakerOpenDurationMilliseconds = -1,
                AllowedExtensions = new[] { ".txt" },
                ExcludePatterns = null!,
                Logging = new SimpleFileLoggerOptions { LogLevel = (LogLevel)999 }
            };

            var result = ExternalConfigurationValidator.Validate(cfg);
            result.IsValid.Should().BeFalse();
            result.Errors.Should().NotBeEmpty();
            result.Errors.Any(e => e.PropertyName == "Folders").Should().BeTrue();
            result.Errors.Any(e => e.PropertyName == nameof(cfg.ProcessedFolder)).Should().BeTrue();
            result.Errors.Any(e => e.PropertyName == nameof(cfg.ChannelCapacity)).Should().BeTrue();
            result.Errors.Any(e => e.PropertyName == nameof(cfg.ExcludePatterns)).Should().BeTrue();
        }

        [Fact]
        public void Validate_accepts_valid_configuration()
        {
            var cfg = new ExternalConfiguration
            {
                Folders = new() { new ExternalConfiguration.WatchedFolderConfig { FolderPath = "c:", ActionName = "a" } },
                Actions = new() { new ExternalConfiguration.ActionConfig { Name = "a", ActionType = ExternalConfiguration.FolderActionType.RestPost, ApiEndpoint = "https://example.com/" } },
                ProcessedFolder = "processed",
                DebounceMilliseconds = 0,
                Retries = 0,
                RetryDelayMilliseconds = 0,
                WatcherMaxRestartAttempts = 1,
                WatcherRestartDelayMilliseconds = 1,
                DiagnosticsUrlPrefix = "http://localhost:5005/",
                ChannelCapacity = 1,
                MaxParallelSends = 1,
                FileWatcherInternalBufferSize = 1024,
                WaitForFileReadyMilliseconds = 0,
                MaxContentBytes = 0,
                StreamingThresholdBytes = 0,
                CircuitBreakerFailureThreshold = 0,
                CircuitBreakerOpenDurationMilliseconds = 0,
                AllowedExtensions = new[] { ".txt" },
                ExcludePatterns = new[] { "*_tmp" },
                Logging = new SimpleFileLoggerOptions { LogLevel = LogLevel.Information }
            };

            var result = ExternalConfigurationValidator.Validate(cfg);
            result.IsValid.Should().BeTrue();
        }
    }
}
