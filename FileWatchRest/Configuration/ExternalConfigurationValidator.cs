namespace FileWatchRest.Configuration;

public sealed class ExternalConfigurationValidator
{
    public static ValidationResult Validate(ExternalConfiguration config)
    {
        var errors = new List<ValidationFailure>();

        if (config.Folders is null || config.Folders.Length == 0)
        {
            errors.Add(new ValidationFailure("Folders", "Folders collection must be present and contain at least one path"));
        }
        else
        {
            for (int i = 0; i < config.Folders.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(config.Folders[i]))
                    errors.Add(new ValidationFailure($"Folders[{i}]", "Folder paths must not be empty"));
            }
        }

        if (string.IsNullOrWhiteSpace(config.ApiEndpoint) || !Uri.TryCreate(config.ApiEndpoint, UriKind.Absolute, out _))
            errors.Add(new ValidationFailure(nameof(config.ApiEndpoint), "ApiEndpoint must be a non-empty absolute URI"));

        if (string.IsNullOrWhiteSpace(config.ProcessedFolder))
            errors.Add(new ValidationFailure(nameof(config.ProcessedFolder), "ProcessedFolder must be provided"));

        if (config.DebounceMilliseconds < 0)
            errors.Add(new ValidationFailure(nameof(config.DebounceMilliseconds), "DebounceMilliseconds must be >= 0"));
        if (config.Retries < 0)
            errors.Add(new ValidationFailure(nameof(config.Retries), "Retries must be >= 0"));
        if (config.RetryDelayMilliseconds < 0)
            errors.Add(new ValidationFailure(nameof(config.RetryDelayMilliseconds), "RetryDelayMilliseconds must be >= 0"));
        if (config.WatcherMaxRestartAttempts < 0)
            errors.Add(new ValidationFailure(nameof(config.WatcherMaxRestartAttempts), "WatcherMaxRestartAttempts must be >= 0"));
        if (config.WatcherRestartDelayMilliseconds < 0)
            errors.Add(new ValidationFailure(nameof(config.WatcherRestartDelayMilliseconds), "WatcherRestartDelayMilliseconds must be >= 0"));

        if (string.IsNullOrWhiteSpace(config.DiagnosticsUrlPrefix) || !Uri.TryCreate(config.DiagnosticsUrlPrefix, UriKind.Absolute, out _))
            errors.Add(new ValidationFailure(nameof(config.DiagnosticsUrlPrefix), "DiagnosticsUrlPrefix must be a non-empty absolute URI"));

        if (config.ChannelCapacity <= 0)
            errors.Add(new ValidationFailure(nameof(config.ChannelCapacity), "ChannelCapacity must be > 0"));
        if (config.MaxParallelSends <= 0)
            errors.Add(new ValidationFailure(nameof(config.MaxParallelSends), "MaxParallelSends must be > 0"));
        if (config.FileWatcherInternalBufferSize <= 0)
            errors.Add(new ValidationFailure(nameof(config.FileWatcherInternalBufferSize), "FileWatcherInternalBufferSize must be > 0"));
        if (config.WaitForFileReadyMilliseconds < 0)
            errors.Add(new ValidationFailure(nameof(config.WaitForFileReadyMilliseconds), "WaitForFileReadyMilliseconds must be >= 0"));
        if (config.MaxContentBytes < 0)
            errors.Add(new ValidationFailure(nameof(config.MaxContentBytes), "MaxContentBytes must be >= 0"));
        if (config.StreamingThresholdBytes < 0)
            errors.Add(new ValidationFailure(nameof(config.StreamingThresholdBytes), "StreamingThresholdBytes must be >= 0"));

        if (config.CircuitBreakerFailureThreshold < 0)
            errors.Add(new ValidationFailure(nameof(config.CircuitBreakerFailureThreshold), "CircuitBreakerFailureThreshold must be >= 0"));
        if (config.CircuitBreakerOpenDurationMilliseconds < 0)
            errors.Add(new ValidationFailure(nameof(config.CircuitBreakerOpenDurationMilliseconds), "CircuitBreakerOpenDurationMilliseconds must be >= 0"));

        if (config.AllowedExtensions is null)
            errors.Add(new ValidationFailure(nameof(config.AllowedExtensions), "AllowedExtensions must be present"));
        else
        {
            for (int i = 0; i < config.AllowedExtensions.Length; i++)
            {
                var ext = config.AllowedExtensions[i];
                if (!string.IsNullOrWhiteSpace(ext) && !ext.StartsWith('.'))
                    errors.Add(new ValidationFailure($"AllowedExtensions[{i}]", "Allowed extensions must start with a '.' or be empty"));
            }
        }

        var allowedLevels = new[] { "Trace", "Debug", "Information", "Warning", "Error", "Critical", "None" };
        var configuredLog = config.Logging?.LogLevel ?? string.Empty;
        if (!allowedLevels.Contains(configuredLog))
            errors.Add(new ValidationFailure("Logging.LogLevel", "LogLevel must be one of Trace, Debug, Information, Warning, Error, Critical, None"));

        return new ValidationResult(errors);
    }
}

public sealed class ValidationResult
{
    public ValidationResult(IEnumerable<ValidationFailure> failures)
    {
        Errors = failures.ToList();
        IsValid = !Errors.Any();
    }

    public bool IsValid { get; }
    public IReadOnlyList<ValidationFailure> Errors { get; }
}

public sealed class ValidationFailure
{
    public ValidationFailure(string propertyName, string errorMessage)
    {
        PropertyName = propertyName;
        ErrorMessage = errorMessage;
    }

    public string PropertyName { get; }
    public string ErrorMessage { get; }
}
