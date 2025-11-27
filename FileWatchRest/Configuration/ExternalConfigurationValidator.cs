namespace FileWatchRest.Configuration;

public sealed class ExternalConfigurationValidator {
    public static ValidationResult Validate(ExternalConfiguration config) {
        var errors = new List<ValidationFailure>();
        // Validate Folders (merged actions)
        if (config.Folders is null || config.Folders.Count == 0) {
            errors.Add(new ValidationFailure("Folders", "Folders collection must be present and contain at least one path"));
        }
        else {
            for (int i = 0; i < config.Folders.Count; i++) {
                ExternalConfiguration.WatchedFolderConfig folder = config.Folders[i];
                if (string.IsNullOrWhiteSpace(folder.FolderPath)) {
                    errors.Add(new ValidationFailure($"Folders[{i}].FolderPath", "FolderPath must not be empty"));
                }
                // Validate ActionType (enum)
                if (!Enum.IsDefined(folder.ActionType)) {
                    errors.Add(new ValidationFailure($"Folders[{i}].ActionType", $"Unknown ActionType '{folder.ActionType}'"));
                }
                else {
                    switch (folder.ActionType) {
                        case ExternalConfiguration.FolderActionType.RestPost:
                            // No extra validation needed for RestPost
                            break;
                        case ExternalConfiguration.FolderActionType.PowerShellScript:
                            if (string.IsNullOrWhiteSpace(folder.ScriptPath)) {
                                errors.Add(new ValidationFailure($"Folders[{i}].ScriptPath", "ScriptPath must be provided for PowerShellScript actions"));
                            }

                            break;
                        case ExternalConfiguration.FolderActionType.Executable:
                            if (string.IsNullOrWhiteSpace(folder.ExecutablePath)) {
                                errors.Add(new ValidationFailure($"Folders[{i}].ExecutablePath", "ExecutablePath must be provided for Executable actions"));
                            }

                            break;
                        default:
                            break;
                    }
                }
            }
        }

        if (string.IsNullOrWhiteSpace(config.ApiEndpoint) || !Uri.TryCreate(config.ApiEndpoint, UriKind.Absolute, out _)) {
            errors.Add(new ValidationFailure(nameof(config.ApiEndpoint), "ApiEndpoint must be a non-empty absolute URI"));
        }

        if (string.IsNullOrWhiteSpace(config.ProcessedFolder)) {
            errors.Add(new ValidationFailure(nameof(config.ProcessedFolder), "ProcessedFolder must be provided"));
        }

        if (config.DebounceMilliseconds < 0) {
            errors.Add(new ValidationFailure(nameof(config.DebounceMilliseconds), "DebounceMilliseconds must be >= 0"));
        }

        if (config.Retries < 0) {
            errors.Add(new ValidationFailure(nameof(config.Retries), "Retries must be >= 0"));
        }

        if (config.RetryDelayMilliseconds < 0) {
            errors.Add(new ValidationFailure(nameof(config.RetryDelayMilliseconds), "RetryDelayMilliseconds must be >= 0"));
        }

        if (config.WatcherMaxRestartAttempts < 0) {
            errors.Add(new ValidationFailure(nameof(config.WatcherMaxRestartAttempts), "WatcherMaxRestartAttempts must be >= 0"));
        }

        if (config.WatcherRestartDelayMilliseconds < 0) {
            errors.Add(new ValidationFailure(nameof(config.WatcherRestartDelayMilliseconds), "WatcherRestartDelayMilliseconds must be >= 0"));
        }

        // DiagnosticsUrlPrefix is optional; validate only when provided
        if (!string.IsNullOrWhiteSpace(config.DiagnosticsUrlPrefix) && !Uri.TryCreate(config.DiagnosticsUrlPrefix, UriKind.Absolute, out _)) {
            errors.Add(new ValidationFailure(nameof(config.DiagnosticsUrlPrefix), "DiagnosticsUrlPrefix, if provided, must be an absolute URI"));
        }

        if (config.ChannelCapacity <= 0) {
            errors.Add(new ValidationFailure(nameof(config.ChannelCapacity), "ChannelCapacity must be > 0"));
        }

        if (config.MaxParallelSends <= 0) {
            errors.Add(new ValidationFailure(nameof(config.MaxParallelSends), "MaxParallelSends must be > 0"));
        }

        if (config.FileWatcherInternalBufferSize <= 0) {
            errors.Add(new ValidationFailure(nameof(config.FileWatcherInternalBufferSize), "FileWatcherInternalBufferSize must be > 0"));
        }

        if (config.WaitForFileReadyMilliseconds < 0) {
            errors.Add(new ValidationFailure(nameof(config.WaitForFileReadyMilliseconds), "WaitForFileReadyMilliseconds must be >= 0"));
        }

        if (config.MaxContentBytes < 0) {
            errors.Add(new ValidationFailure(nameof(config.MaxContentBytes), "MaxContentBytes must be >= 0"));
        }

        if (config.StreamingThresholdBytes < 0) {
            errors.Add(new ValidationFailure(nameof(config.StreamingThresholdBytes), "StreamingThresholdBytes must be >= 0"));
        }

        if (config.CircuitBreakerFailureThreshold < 0) {
            errors.Add(new ValidationFailure(nameof(config.CircuitBreakerFailureThreshold), "CircuitBreakerFailureThreshold must be >= 0"));
        }

        if (config.CircuitBreakerOpenDurationMilliseconds < 0) {
            errors.Add(new ValidationFailure(nameof(config.CircuitBreakerOpenDurationMilliseconds), "CircuitBreakerOpenDurationMilliseconds must be >= 0"));
        }

        // AllowedExtensions is optional; empty means no extension filtering
        if (config.AllowedExtensions is not null) {
            for (int i = 0; i < config.AllowedExtensions.Length; i++) {
                string ext = config.AllowedExtensions[i];
                if (!string.IsNullOrWhiteSpace(ext) && !ext.StartsWith('.')) {
                    errors.Add(new ValidationFailure($"AllowedExtensions[{i}]", "Allowed extensions must start with a '.' or be empty"));
                }
            }
        }
        if (config.ExcludePatterns is null) {
            errors.Add(new ValidationFailure(nameof(config.ExcludePatterns), "ExcludePatterns must be present"));
        }

        string[] allowedLevels = ["Trace", "Debug", "Information", "Warning", "Error", "Critical", "None"];
        string configuredLog = config.Logging?.LogLevel.ToString() ?? string.Empty;
        if (!allowedLevels.Contains(configuredLog)) {
            errors.Add(new ValidationFailure("Logging.LogLevel", "LogLevel must be one of Trace, Debug, Information, Warning, Error, Critical, None"));
        }

        return new ValidationResult(errors);
    }
}

public sealed class ValidationResult {
    public ValidationResult(IEnumerable<ValidationFailure> failures) {
        Errors = [.. failures];
        IsValid = !Errors.Any();
    }

    public bool IsValid { get; }
    public IReadOnlyList<ValidationFailure> Errors { get; }
}

public sealed class ValidationFailure(string propertyName, string errorMessage) {
    public string PropertyName { get; } = propertyName;
    public string ErrorMessage { get; } = errorMessage;
}
