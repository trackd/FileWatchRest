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

                if (string.IsNullOrWhiteSpace(folder.ActionName)) {
                    errors.Add(new ValidationFailure($"Folders[{i}].ActionName", "ActionName must be provided for each folder and reference an Action in Actions[]"));
                }
                else {
                    // Ensure referenced action exists
                    if (config.Actions?.Any(a => string.Equals(a.Name, folder.ActionName, StringComparison.OrdinalIgnoreCase)) != true) {
                        errors.Add(new ValidationFailure($"Folders[{i}].ActionName", $"Folder references unknown Action '{folder.ActionName}'"));
                    }
                }
            }
        }

        // Additionally, require a top-level ApiEndpoint when REST actions depend on it.
        List<ExternalConfiguration.ActionConfig> restActions = config.Actions?.Where(a => a.ActionType == ExternalConfiguration.FolderActionType.RestPost).ToList() ?? [];
        bool topLevelApiValid = !string.IsNullOrWhiteSpace(config.ApiEndpoint) && Uri.TryCreate(config.ApiEndpoint, UriKind.Absolute, out _);
        if (restActions.Count > 0) {
            // If any RestPost action does not define its own ApiEndpoint, require a valid top-level ApiEndpoint
            if (!topLevelApiValid && restActions.Any(a => string.IsNullOrWhiteSpace(a.ApiEndpoint))) {
                errors.Add(new ValidationFailure(nameof(config.ApiEndpoint), "ApiEndpoint must be a non-empty absolute URI when REST actions rely on the top-level default"));
            }
        }

        // Validate each ActionConfig and any per-action overrides
        if (config.Actions is not null) {
            for (int ai = 0; ai < config.Actions.Count; ai++) {
                ValidateActionConfig(config.Actions[ai], ai, errors);
            }
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
        ValidateExtensions(config.AllowedExtensions, "AllowedExtensions", errors);

        // Helper: validate each action config with reusable checks
        static void ValidateActionConfig(ExternalConfiguration.ActionConfig action, int ai, List<ValidationFailure> errors) {
            if (string.IsNullOrWhiteSpace(action.Name)) {
                errors.Add(new ValidationFailure($"Actions[{ai}].Name", "Action name must be provided"));
                return;
            }

            // Action-type specific required fields
            switch (action.ActionType) {
                case ExternalConfiguration.FolderActionType.RestPost:
                    ValidateUriIfPresent(action.ApiEndpoint, $"Actions[{ai}].ApiEndpoint", errors);
                    break;
                case ExternalConfiguration.FolderActionType.PowerShellScript:
                    if (string.IsNullOrWhiteSpace(action.ScriptPath)) {
                        errors.Add(new ValidationFailure($"Actions[{ai}].ScriptPath", "ScriptPath must be provided for PowerShellScript actions"));
                    }
                    break;
                case ExternalConfiguration.FolderActionType.Executable:
                    if (string.IsNullOrWhiteSpace(action.ExecutablePath)) {
                        errors.Add(new ValidationFailure($"Actions[{ai}].ExecutablePath", "ExecutablePath must be provided for Executable actions"));
                    }
                    break;
                default:
                    break;
            }

            // Validate per-action allowed extensions format
            ValidateExtensions(action.AllowedExtensions, $"Actions[{ai}].AllowedExtensions", errors);

            // Validate per-action exclude patterns (if present, ensure non-null entries)
            if (action.ExcludePatterns is not null) {
                for (int j = 0; j < action.ExcludePatterns.Length; j++) {
                    string pat = action.ExcludePatterns[j];
                    if (pat is null) {
                        errors.Add(new ValidationFailure($"Actions[{ai}].ExcludePatterns[{j}]", "ExcludePatterns entries must not be null"));
                    }
                }
            }

            // Validate arguments array entries (if present)
            if (action.Arguments is not null) {
                for (int j = 0; j < action.Arguments.Count; j++) {
                    if (action.Arguments[j] is null) {
                        errors.Add(new ValidationFailure($"Actions[{ai}].Arguments[{j}]", "Argument entries must not be null"));
                    }
                }
            }
        }

        static void ValidateUriIfPresent(string? uriValue, string propertyName, List<ValidationFailure> errors) {
            if (!string.IsNullOrWhiteSpace(uriValue) && !Uri.TryCreate(uriValue, UriKind.Absolute, out _)) {
                errors.Add(new ValidationFailure(propertyName, "Value must be an absolute URI when provided"));
            }
        }

        static void ValidateExtensions(string[]? extensions, string propertyPrefix, List<ValidationFailure> errors) {
            if (extensions is null) return;
            for (int i = 0; i < extensions.Length; i++) {
                string ext = extensions[i];
                if (!string.IsNullOrWhiteSpace(ext) && !ext.StartsWith('.')) {
                    errors.Add(new ValidationFailure($"{propertyPrefix}[{i}]", "Allowed extensions must start with a '.' or be empty"));
                }
            }
        }
        if (config.ExcludePatterns is null) {
            errors.Add(new ValidationFailure(nameof(config.ExcludePatterns), "ExcludePatterns must be present"));
        }

        string[] allowedLevels = Enum.GetNames<LogLevel>();
        string configuredLog = config.Logging?.LogLevel.ToString() ?? string.Empty;
        if (!allowedLevels.Contains(configuredLog)) {
            errors.Add(new ValidationFailure("Logging.LogLevel", $"LogLevel must be one of {string.Join(", ", allowedLevels)}"));
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
