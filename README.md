<h1 align="center">FileWatchRest</h1>  
<div align="center">  
   <sub>  

   FileWatch Windows Service that can take actions on files.  
   </sub>  
<br/><br/>  

[![build](https://github.com/trackd/FileWatchRest/actions/workflows/ci.yml/badge.svg)](https://github.com/trackd/FileWatchRest/actions/workflows/ci.yml)
[![codecov](https://codecov.io/github/trackd/FileWatchRest/graph/badge.svg?token=7H2MHCOP0G)](https://codecov.io/github/trackd/FileWatchRest)
[![LICENSE](https://img.shields.io/github/license/trackd/FileWatchRest)](https://github.com/trackd/FileWatchRest/blob/main/LICENSE)
</div>  

Modern Windows service that watches folders for new or changed files and POSTs file information (and  
optionally file contents) to a configured HTTP REST API  

Key Features  
  
- **Multi-folder watching** with real-time configuration updates and per-folder actions
- **BackgroundService architecture** with dedicated debouncing and sending services
- **Bearer token authentication** for secure API communication with automatic encryption
- **File content processing** with streaming support for large files
- **Extension-based filtering** and wildcard pattern exclusion
- **Automatic file archiving** to processed folders after successful API calls
- **Debounced detection** with low-latency posting using bounded channels and ArrayPool<T>
- **Robust error handling** with configurable retry logic, circuit breaker, and restart mechanisms
- **Real-time diagnostics** with structured CSV/JSON logging and HTTP metrics endpoints
- **Native AOT ready** for high-performance deployment

Project Structure  
  
The codebase is organized into logical folders following modern .NET patterns:

```md
FileWatchRest/
├── Configuration/     # Configuration management and validation
│   ├── ExternalConfiguration.cs
│   ├── ConfigurationService.cs
│   ├── ExternalConfigurationValidator.cs
│   └── SecureConfigurationHelper.cs (token encryption)
├── Services/          # BackgroundService implementations
│   ├── Worker.cs (orchestration)
│   ├── FileDebounceService.cs (dedicated debouncing)
│   ├── FileSenderService.cs (dedicated sending)
│   ├── FileWatcherManager.cs (watcher lifecycle)
│   ├── HttpResilienceService.cs (retry + circuit breaker)
│   └── DiagnosticsService.cs (metrics + HTTP endpoints)
├── Monitor/           # IOptionsMonitor pattern implementation
│   └── ExternalConfigurationOptionsMonitor.cs
├── Models/            # Data models with System.Text.Json source generation
│   ├── FileNotification.cs
│   ├── UploadMetadata.cs
│   └── JsonContexts.cs
├── Logging/           # Custom structured logging
│   ├── SimpleFileLoggerProvider.cs (CSV/JSON)
│   └── LoggerDelegates.cs (LoggerMessage pattern)
├── Helpers/           # Utility classes
│   └── WildcardPatternMatcher.cs
└── Program.cs         # Application entry point
```

Configuration  
  
The service uses a single JSON configuration file for all settings:

**Configuration File**: `$env:ProgramData\FileWatchRest\FileWatchRest.json`  

This file is created automatically with defaults and can be edited while the service is running.  
Changes are detected automatically and applied without restarting the service.  

Example configuration (typed `Folders` + `Actions`):

```json
{
  "Folders": [
    {
      "FolderPath": "C:\\temp\\watch",
      "ActionName": "RestEndpoint1"
    },
    {
      "FolderPath": "C:\\data\\incoming",
      "ActionName": "ObjectScript"
    }
  ],
  "Actions": [
    {
      "Name": "RestEndpoint1",
      "ActionType": "RestPost",
      "ApiEndpoint": "https://api.example.com/files",
      "BearerToken": "your-bearer-token-here"
    },
    {
      "Name": "ObjectScript",
      "ActionType": "PowerShellScript",
      "ScriptPath": "C:\\scripts\\processObject.ps1",
      "Arguments": [
        "{FileNotification:json}"
      ],
      "IncludeSubdirectories": true
    }
  ],
  "ApiEndpoint": "https://api.example.com/files",
  "BearerToken": "your-bearer-token-here-will-be-encrypted-automatically",
  "PostFileContents": true,
  "MoveProcessedFiles": true,
  "ProcessedFolder": "processed",
  "AllowedExtensions": [
    ".txt",
    ".json",
    ".xml"
  ],
  "IncludeSubdirectories": true,
  "DebounceMilliseconds": 1000,
  "Retries": 3,
  "RetryDelayMilliseconds": 500,
  "WatcherMaxRestartAttempts": 3,
  "WatcherRestartDelayMilliseconds": 1000,
  "DiagnosticsUrlPrefix": "http://localhost:5005/",
  "DiagnosticsBearerToken": null,
  "ChannelCapacity": 1000,
  "MaxParallelSends": 4,
  "FileWatcherInternalBufferSize": 65536,
  "WaitForFileReadyMilliseconds": 0,
  "DiscardZeroByteFiles": false,
  "MaxContentBytes": 5242880,
  "StreamingThresholdBytes": 262144,
  "EnableCircuitBreaker": false,
  "CircuitBreakerFailureThreshold": 5,
  "CircuitBreakerOpenDurationMilliseconds": 30000,
  "Logging": {
    "LogType": "Csv",
    "FilePathPattern": "logs/FileWatchRest_{0:yyyyMMdd_HHmmss}",
    "LogLevel": "Information",
    "RetainedDays": 14
  }
}
```

Config file overrides  

You can override the default configuration file path when starting the service or in your environment:

- **Command-line:** pass `--config <path>` or `-c <path>` to specify the configuration file to use.
- **Environment variable:** set `FILEWATCHREST_CONFIG` to a file path and it will be used when no `--config` arg is provided.

If neither is provided the service falls back to the default file under `$env:ProgramData\FileWatchRest\FileWatchRest.json`.  

## Additional Configuration Examples

Below are a few small example configurations demonstrating common patterns (minimal REST action, reusable PowerShell action, mixed action types, and legacy string-array folders). These are provided here for convenience; full example files are in the `examples/` folder and a single runnable template is in `FileWatchRest.json.example`.  

Example files (in-repo):

- `examples/FileWatchRest.example.minimal.json`: minimal REST-only example
- `examples/FileWatchRest.example.powershell.json`: reusable PowerShell action example
- `examples/FileWatchRest.example.mixed.json`: mixed executable + REST example

Use these as starting points — copy the one you need to `FileWatchRest.json` (or point the service to it with `--config`).  

### 1) Minimal single REST action (simple)

```json
{
  "Folders": [
    { 
      "FolderPath": "C:\\temp\\watch", 
      "ActionName": "RestDefault" 
    }
  ],
  "Actions": [
    {
      "Name": "RestDefault",
      "ActionType": "RestPost",
      "ApiEndpoint": "https://api.example.com/files"
    }
  ]
}
```

### 2) PowerShell script per-folder (reusable actions)

```json
{
  "Folders": [
    { 
      "FolderPath": "C:\\data\\incoming", 
      "ActionName": "ParseAndTransform" 
    },
    { 
      "FolderPath": "C:\\data\\incoming\\objects", 
      "ActionName": "ParseAndTransform" 
    }
  ],
  "Actions": [
    {
      "Name": "ParseAndTransform",
      "ActionType": "PowerShellScript",
      "ScriptPath": "C:\\scripts\\processObject.ps1",
      "Arguments": ["{FileNotification:json}"],
      "IncludeSubdirectories": true,
      "AllowedExtensions": [".json", ".xml"]
    }
  ]
}
```

### 3) Mixed action types: script, executable, and REST with per-action overrides

```json
{
  "Folders": [
    { 
      "FolderPath": "C:\\apps\\drop", 
      "ActionName": "RunExe" 
    },
    { 
      "FolderPath": "C:\\invoices", 
      "ActionName": "PostInvoices" 
    }
  ],
  "Actions": [
    {
      "Name": "RunExe",
      "ActionType": "Executable",
      "ExecutablePath": "C:\\tools\\processor.exe",
      "Arguments": ["--input", "{FilePath}"],
      "MoveProcessedFiles": true,
      "ProcessedFolder": "processed_exe"
    },
    {
      "Name": "PostInvoices",
      "ActionType": "RestPost",
      "ApiEndpoint": "https://invoices.example.com/upload",
      "BearerToken": "<encrypted-or-plain-token>",
      "PostFileContents": true,
      "AllowedExtensions": [".pdf", ".docx"],
      "Retries": 5
    }
  ]
}
```

### 4) Legacy compatible: string-array `Folders` (migration)

The monitor accepts the legacy `Folders: ["C:\\path"]` string-array format and will migrate it into the typed object format during load. Prefer the typed object form for clarity and reusability.  

Configuration Options  
  
**Core File Watching Settings:**  

- `Folders`: Array of typed folder objects. Each entry must include `FolderPath` and a reference to a named action via `ActionName`. Folders are lightweight mappings that reference reusable `Actions[]` entries which define processing behavior. Example:

```json
"Folders": [
  {
    "FolderPath": "C:\\temp\\watch",
    "ActionName": "RestEndpoint1"
  },
  {
    "FolderPath": "C:\\data\\incoming",
    "ActionName": "ObjectScript"
  }
]
```

- `Actions`: Array of named `ActionConfig` objects. Each action is a complete, reusable processing configuration (action type, script/executable paths, REST endpoint, bearer token, file handling options, retries, circuit breaker settings, etc.). Example:

```json
"Actions": [
  {
    "Name": "RestEndpoint1",
    "ActionType": "RestPost",
    "ApiEndpoint": "https://api.example.com/files"
  },
  {
    "Name": "ObjectScript",
    "ActionType": "PowerShellScript",
    "ScriptPath": "C:\\scripts\\processObject.ps1"
  }
]
```

Precedence and overrides:

- Settings on an `ActionConfig` override global settings for any folder mapped to that action.
- Global (root) settings are defaults used when an action does not specify a value.
- `Folders` are intentionally lightweight (path + `ActionName`) and do not carry overrides.
- Arrays and null/empty semantics:
  - If a global collection (e.g., `AllowedExtensions`, `ExcludePatterns`) is null or empty, it means “no filtering” unless the action provides values.
  - If an action provides a collection, it fully defines the behavior for that folder mapping.
  - If an action explicitly provides an empty collection, it disables that filter for that action (e.g., empty `AllowedExtensions` means all files allowed).
- `ApiEndpoint`: HTTP endpoint to POST file notifications to
- `BearerToken`: Bearer token for API authentication. **Automatically encrypted** using
  machine-specific encryption when saved. Plain text tokens are automatically encrypted on first  
  save.  
- `PostFileContents`: If true, reads and includes file contents in the POST
- `ExecutionTimeoutMilliseconds`: Optional per-action timeout in milliseconds. When set, the action's process will be terminated if it runs longer than this duration. Default: 60000 (60s).
- `IgnoreOutput`: Optional boolean. When true the action will not capture or log stdout/stderr (they are not redirected). Use this to avoid buffering or logging large outputs. Default: false.
- `MoveProcessedFiles`: If true, moves files to processed folder after successful POST
- `ProcessedFolder`: Name of subfolder to move processed files to (default: "processed"). Files in
  this folder are automatically excluded from monitoring to prevent infinite loops.  
- `AllowedExtensions`: Array of file extensions to watch (empty = all files)
- `ExcludePatterns`: Array of filename patterns to exclude from processing. Supports wildcard
  matching with `*` (any characters) and `?` (single character). Examples: `"Backup_*"` (starts with  
  Backup\_), `"*_temp"` (ends with \_temp), `"*.bak"` (backup files). Files matching any exclude  
  pattern are ignored even if they pass extension filtering.  
- `IncludeSubdirectories`: Whether to watch subfolders
- `DebounceMilliseconds`: Wait time to debounce file events

**Performance and Reliability Settings:**  

- `Retries`: Number of retry attempts for failed API calls (default: 3)
- `RetryDelayMilliseconds`: Delay between retry attempts (default: 500)
- `WatcherMaxRestartAttempts`: Max attempts to restart a failed file watcher (default: 3)
- `WatcherRestartDelayMilliseconds`: Delay before restarting a watcher (default: 1000)
- `DiagnosticsUrlPrefix`: URL prefix for diagnostics endpoint (default: "<http://localhost:5005/>")
- `DiagnosticsBearerToken`: Optional bearer token required to access diagnostics endpoints. If
  null or empty, diagnostics endpoints are accessible without authentication. No token is generated automatically.  
- `ChannelCapacity`: Internal channel capacity for pending file events (default: 1000)
- `MaxParallelSends`: Number of concurrent HTTP senders (default: 4)
- `FileWatcherInternalBufferSize`: FileSystemWatcher buffer size in bytes (default: 65536)
- `WaitForFileReadyMilliseconds`: Wait time for files to become ready before processing (default: 0)
- `DiscardZeroByteFiles`: If true, files that remain zero bytes after waiting the configured
  `WaitForFileReadyMilliseconds` will be discarded and not posted. Default: false. Use this when  
  producers create zero-length placeholder files that should be ignored.  
- `MaxContentBytes`: Maximum bytes of file content to include in the POST request. Files larger than
  this are sent without inline content.  
- `StreamingThresholdBytes`: Size threshold for switching to streaming uploads. Files larger than
  this use multipart streaming for uploads.  
- `EnableCircuitBreaker`: Enables an optional circuit breaker for HTTP calls. When enabled, the
  circuit breaker trips after a number of failures, temporarily blocking requests to allow the  
  remote service to recover.  
- `CircuitBreakerFailureThreshold`: Number of consecutive failures required to trip the circuit
  breaker (default: 5).  
- `CircuitBreakerOpenDurationMilliseconds`: Time duration in milliseconds to keep the circuit
  breaker open before allowing retries (default: 30000).  

`System.Security.Cryptography.ProtectedData` with machine-specific encryption. This means:

- Plain text bearer tokens are automatically encrypted when the configuration is first saved
- Encrypted tokens can only be decrypted on the same machine by the same application
- Configuration files are safe to store in version control (tokens are encrypted)
- No master password or key management required - Windows handles the encryption keys

**Migration Support**: Existing plain text tokens are automatically detected and encrypted on the  
next configuration save without requiring user intervention.  

Development and Testing  
  
Run locally from repository root:

```powershell
# Build
dotnet build FileWatchRest.sln  

# Run tests (123 comprehensive tests)
dotnet test FileWatchRest.sln

# Run with coverage
dotnet test FileWatchRest.sln --collect:"XPlat Code Coverage"

# Run as console for testing
dotnet run --project .\FileWatchRest\FileWatchRest.csproj  
```

Packaging for Deployment  
  
Prepare a deployment package (creates `./output` by default):

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -ProjectPath FileWatchRest -OutputDir .\output  
```

The script automatically creates a deployment package with `install_on_target.ps1`.  

Installation on Target Machine  
  
1. Copy the entire `output` folder to the target machine
2. As Administrator, run from inside the `output` folder:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass .\install_on_target.ps1  
```

This installs files to `$env:ProgramFiles\FileWatchRest`, creates and starts the Windows service,  
and sets up the configuration directory under `$env:ProgramData\FileWatchRest`.  

API Payload Format  
  
The service POSTs JSON data to your configured endpoint:

Basic Notification (metadata)  
  
```json
{  
  "Path": "C:\\temp\\watch\\example.txt",  
  "Content": null,  
  "ComputerName": "Server1",
  "FileSize": 1024,  
  "LastWriteTime": "2025-09-17T10:30:00"  
}  
```

Full Notification (with content)  
  
```json
{  
  "Path": "C:\\temp\\watch\\example.txt",  
  "Content": "file content here...",  
  "ComputerName": "Server2",
  "FileSize": 1024,  
  "LastWriteTime": "2025-09-17T10:30:00"  
}  
```

📊 Diagnostics Endpoints  
  
The service provides a built-in HTTP server for real-time diagnostics and monitoring. The server  
runs on the URL specified by `DiagnosticsUrlPrefix` (default: `http://localhost:5005/`).  

Diagnostics Endpoints  
  
| Endpoint | Description | Response Format |
|----------|-------------|-----------------|
| `GET /` | Complete service status (same as `/status`) | JSON |
| `GET /status` | Full service metrics and diagnostics | JSON |
| `GET /health` | Simple health check | JSON |
| `GET /events` | Recent file processing events (last 500) | JSON |
| `GET /watchers` | Currently active folder watchers | JSON |
| `GET /config` | Current runtime configuration (normalized) | JSON |
| `GET /metrics` | Prometheus-compatible metrics | Text |
| `GET /circuits` | Circuit breaker states per endpoint | JSON |

Examples  
  
GET /status  
  
```json
{
  "ActiveWatchers": ["C:\\temp\\watch", "C:\\data\\incoming"],
  "RestartAttempts": {},
  "RecentEvents": [
    {
      "Path": "C:\\temp\\watch\\document.txt",
      "Timestamp": "2025-09-18T14:30:22.123Z",
      "PostedSuccess": true,
      "StatusCode": 200
    }
  ],
  "Timestamp": "2025-09-18T14:30:22.456Z",
  "EventCount": 15
}
```

GET /health  
  
```json
{
  "status": "healthy",
  "timestamp": "2025-09-18T14:30:22.456Z"
}
```

GET /events  
  
```json
[
  {
    "Path": "C:\\temp\\watch\\file1.txt",
    "Timestamp": "2025-09-18T14:30:22.123Z",
    "PostedSuccess": true,
    "StatusCode": 200
  },
  {
    "Path": "C:\\temp\\watch\\file2.txt", 
    "Timestamp": "2025-09-18T14:29:15.456Z",
    "PostedSuccess": false,
    "StatusCode": 500
  }
]
```

**Features:**  

- **Lightweight**: HttpListener-based, no ASP.NET Core overhead
- **Real-time monitoring**: See file processing events as they happen
- **CORS enabled**: Browser-accessible from any origin  
- **Error tracking**: Monitor failed API calls and retry attempts
- **Service health**: Quick health checks for monitoring systems

**Usage:**  

1. Start the FileWatchRest service
2. Open browser to `http://localhost:5005/status`
3. Monitor file processing in real-time
4. Use `/events` for troubleshooting failed file processing

Security and accessing diagnostics  
  
By default, diagnostics endpoints are **unauthenticated** and accessible without credentials. To secure them, set `DiagnosticsBearerToken` in your configuration:

```json
{
  "DiagnosticsBearerToken": "your-secret-token-here"
}
```

When a token is configured, all diagnostics endpoints require a matching Authorization header:

```bash
curl -H "Authorization: Bearer your-secret-token-here" http://localhost:5005/status
```

**Note**: If you configure a token, it will be automatically encrypted using Windows machine-specific encryption when the configuration is saved, just like API bearer tokens.  

Logging  
  
Logging is configured from the same external configuration file used by the service  
(`$env:ProgramData\\FileWatchRest\\FileWatchRest.json`)  

configuration file. By default the service emits CSV logs and JSON is opt-in. The configuration  
focuses on a single file name/pattern and an explicit `LogType` selector. The provider automatically  
appends the correct file extension based on the `LogType` value.  
The service uses modern structured logging with LoggerMessage delegates for zero-allocation logging.  
You can select CSV, JSON, or both via the `Logging` / `LoggingOptions` settings in the configuration  
file. By default the service emits CSV logs and JSON is opt-in. The configuration focuses on a single  
file name/pattern and an explicit `LogType` selector. The provider automatically appends the correct  
file extension based on the `LogType` value.  

Default logging locations (per-run timestamped by default):

- `$env:ProgramData\\FileWatchRest\\logs\\FileWatchRest_{0:yyyyMMdd_HHmmss}.csv` (structured CSV)
- `$env:ProgramData\\FileWatchRest\\logs\\FileWatchRest_{0:yyyyMMdd_HHmmss}.ndjson` (structured
  JSON)  

Example `Logging` section (place this in the external configuration `FileWatchRest.json`):

```json
"Logging": {
  "LogType": "Csv",        // One of: "Csv", "Json", "Both"
  "FilePathPattern": "logs/FileWatchRest_{0:yyyyMMdd_HHmmss}",
  "LogLevel": "Information",
  "RetainedDays": 14
}
```

Notes:

- `LogType` selects which file formats the built-in provider writes. The provider automatically
  appends the appropriate extension (`.csv` for Csv, `.ndjson` for Json).  
- `LogLevel` can be adjusted at runtime via the configuration file; note that changing the log file
  target (FilePathPattern or LogType) typically requires a service restart for the file provider to  
  open new files.  

Troubleshooting  
  
Service Won't Start  
  
- Run the executable directly from command prompt to see console errors
- Check Windows Event Log for startup failures
- Verify configuration file exists and is valid JSON

Files Not Being Detected  
  
- Check that folder paths in configuration exist and are accessible
- Verify file extensions match `AllowedExtensions` if specified
- Review logs (JSON/CSV) for watcher errors or restart attempts
- **Note**: Files in folders matching the `ProcessedFolder` configuration value are automatically
  ignored to prevent infinite processing loops  

API Calls Failing  
  
- Verify `ApiEndpoint` is correct and accessible
- Check `BearerToken` if API requires authentication
- Review retry settings in `appsettings.json`
- Check logs (JSON/CSV) for HTTP status codes

Native AOT Deployment  
  
For high-performance deployment with Native AOT:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

**Requirements**: Visual C++ build tools must be installed on the build machine for Native AOT  
compilation.  

Configuration Management  
  
- **Single Configuration File**: All settings are now in one place - `FileWatchRest.json`
- Configuration changes are detected automatically - no service restart required
- Invalid JSON will cause service to use previous valid configuration
- Default configuration is created automatically on first run
- Configuration file can be edited manually or through automated deployment scripts
- No need for separate `appsettings.json` modifications

# Test version bump
