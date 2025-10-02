FileWatchRest  
=============  

Modern Windows service that watches folders for new or changed files and POSTs file information (and optionally file contents) to a configured HTTP REST API  

Key Features  
------------  

- **Multi-folder watching** with real-time configuration updates
- **Bearer token authentication** for secure API communication
- **File content processing** with configurable options
- **Extension-based filtering** to watch only specific file types
- **Automatic file archiving** to processed folders after successful API calls
- **Debounced detection** with low-latency posting using bounded channels
- **Robust error handling** with configurable retry logic and r restart mechanisms
- **Real-time diagnostics** with structured CSV logging
- **Native AOT ready** for high-performance deployment

Project Structure  
-----------------  

The codebase is organized into logical folders for better maintainability:

```note
FileWatchRest/
├── Configuration/     # Configuration management classes
│   ├── ExternalConfiguration.cs
│   └── ConfigurationService.cs
├── Services/          # Core service implementations  
│   ├── Worker.cs
│   └── DiagnosticsService.cs
├── Models/           # Data models and JSON contexts
│   ├── FileNotification.cs
│   └── JsonContexts.cs
├── Logging/          # Logging implementations
│   └── SimpleFileLoggerProvider.cs (custom file + CSV logger)
└── Program.cs        # Application entry point
```

Configuration  
-------------  

The service uses a single JSON configuration file for all settings:

**Configuration File**: `$env:ProgramData\FileWatchRest\FileWatchRest.json`  

This file is created automatically with defaults and can be edited while the service is running. Changes are detected automatically and applied without restarting the service.  

Example configuration:

```json
{
  "Folders": [
    "C:\\temp\\watch",
    "C:\\data\\incoming"
  ],
  "ApiEndpoint": "https://api.example.com/files",
  "BearerToken": "your-bearer-token-here-will-be-encrypted-automatically",
  "PostFileContents": true,
  "MoveProcessedFiles": true,
  "ProcessedFolder": "processed",
  "AllowedExtensions": [
    ".txt",
    ".json",
    ".xml",
    ".csv"
  ],
  "IncludeSubdirectories": true,
  "DebounceMilliseconds": 1000,
  "Retries": 3,
  "RetryDelayMilliseconds": 500,
  "WatcherMaxRestartAttempts": 3,
  "WatcherRestartDelayMilliseconds": 1000,
  "DiagnosticsUrlPrefix": "http://localhost:5005/",
  "ChannelCapacity": 1000,
  "MaxParallelSends": 4,
  "FileWatcherInternalBufferSize": 65536,
  "WaitForFileReadyMilliseconds": 0,
  "MaxContentBytes": 5242880,
  "StreamingThresholdBytes": 262144,
  "EnableCircuitBreaker": false,
  "CircuitBreakerFailureThreshold": 5,
  "CircuitBreakerOpenDurationMilliseconds": 30000,
  "Logging": {
    "LogType": "Csv",
    "FilePathPattern": "logs/FileWatchRest_{0:yyyyMMdd_HHmmss}",
    "LogLevel": "Information",
    "RetainedFileCountLimit": 14
  }
}
```

Configuration Options  
---------------------  

**Core File Watching Settings:**  

- `Folders`: Array of folder paths to watch
- `ApiEndpoint`: HTTP endpoint to POST file notifications to
- `BearerToken`: Bearer token for API authentication. **Automatically encrypted** using machine-specific encryption when saved. Plain text tokens are automatically encrypted on first save.
- `PostFileContents`: If true, reads and includes file contents in the POST
- `MoveProcessedFiles`: If true, moves files to processed folder after successful POST
- `ProcessedFolder`: Name of subfolder to move processed files to (default: "processed"). Files in this folder are automatically excluded from monitoring to prevent infinite loops.
- `AllowedExtensions`: Array of file extensions to watch (empty = all files)
- `IncludeSubdirectories`: Whether to watch subfolders
- `DebounceMilliseconds`: Wait time to debounce file events

**Performance and Reliability Settings:**  

- `Retries`: Number of retry attempts for failed API calls (default: 3)
- `RetryDelayMilliseconds`: Delay between retry attempts (default: 500)
- `WatcherMaxRestartAttempts`: Max attempts to restart a failed file watcher (default: 3)
- `WatcherRestartDelayMilliseconds`: Delay before restarting a watcher (default: 1000)
- `DiagnosticsUrlPrefix`: URL prefix for diagnostics endpoint (default: "<http://localhost:5005/>")
- `ChannelCapacity`: Internal channel capacity for pending file events (default: 1000)
- `MaxParallelSends`: Number of concurrent HTTP senders (default: 4)
- `FileWatcherInternalBufferSize`: FileSystemWatcher buffer size in bytes (default: 65536)
- `WaitForFileReadyMilliseconds`: Wait time for files to become ready before processing (default: 0)
- `MaxContentBytes`: Maximum bytes of file content to include in the POST request. Files larger than this are sent without inline content.
- `StreamingThresholdBytes`: Size threshold for switching to streaming uploads. Files larger than this use multipart streaming for uploads.
- `EnableCircuitBreaker`: Enables an optional circuit breaker for HTTP calls. When enabled, the circuit breaker trips after a number of failures, temporarily blocking requests to allow the remote service to recover.
- `CircuitBreakerFailureThreshold`: Number of consecutive failures required to trip the circuit breaker (default: 5).
- `CircuitBreakerOpenDurationMilliseconds`: Time duration in milliseconds to keep the circuit breaker open before allowing retries (default: 30000).

Security Features  
-----------------  

**Automatic Token Encryption**: Bearer tokens are automatically encrypted using `System.Security.Cryptography.ProtectedData` with machine-specific encryption. This means:

- Plain text bearer tokens are automatically encrypted when the configuration is first saved
- Encrypted tokens can only be decrypted on the same machine by the same application
- Configuration files are safe to store in version control (tokens are encrypted)
- No master password or key management required - Windows handles the encryption keys

**Migration Support**: Existing plain text tokens are automatically detected and encrypted on the next configuration save without requiring user intervention.  

Development and Testing  
-----------------------  

Run locally from repository root:

```powershell
# Build
dotnet build FileWatchRest.sln  

# Run as console for testing
dotnet run --project .\FileWatchRest\FileWatchRest.csproj  
```

Packaging for Deployment  
-------------------------  

Prepare a deployment package (creates `./output` by default):

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -ProjectPath FileWatchRest -OutputDir .\output  
```

The script automatically creates a deployment package with `install_on_target.ps1`.  

Installation on Target Machine  
-------------------------------  

1. Copy the entire `output` folder to the target machine
2. As Administrator, run from inside the `output` folder:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass .\install_on_target.ps1  
```

This installs files to `$env:ProgramFiles\FileWatchRest`, creates and starts the Windows service, and sets up the configuration directory under `$env:ProgramData\FileWatchRest`.  

API Payload Format  
------------------  

The service POSTs JSON data to your configured endpoint:

Basic Notification (metadata)  
-----------------------------  

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
-------------------------------  

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
-----------------------  

The service provides a built-in HTTP server for real-time diagnostics and monitoring. The server runs on the URL specified by `DiagnosticsUrlPrefix` (default: `http://localhost:5005/`).  

Diagnostics Endpoints  
---------------------  

| Endpoint | Description | Response Format |
|----------|-------------|-----------------|
| `GET /` | Complete service status (same as `/status`) | JSON |
| `GET /status` | Full service metrics and diagnostics | JSON |
| `GET /health` | Simple health check | JSON |
| `GET /events` | Recent file processing events (last 500) | JSON |
| `GET /watchers` | Currently active folder watchers | JSON |

Examples  
--------  

**GET /status**  

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

**GET /health**  

```json
{
  "status": "healthy",
  "timestamp": "2025-09-18T14:30:22.456Z"
}
```

**GET /events**  

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

Logging
-------

Logging is configured from the same external configuration file used by the service (`$env:ProgramData\\FileWatchRest\\FileWatchRest.json`)

You can select CSV, JSON, or both via the `Logging` / `LoggingOptions` settings in the configuration file. By default the service emits CSV logs and JSON is opt-in. The configuration now focuses on a single file name/pattern and an explicit `LogType` selector. The provider will append the correct file extension based on the `LogType` value.

Default logging locations (per-run timestamped by default):

- `$env:ProgramData\\FileWatchRest\\logs\\FileWatchRest_{0:yyyyMMdd_HHmmss}.csv` (structured CSV)
- `$env:ProgramData\\FileWatchRest\\logs\\FileWatchRest_{0:yyyyMMdd_HHmmss}.ndjson` (structured JSON)

Example `Logging` section (place this in the external configuration `FileWatchRest.json`):

```json
"Logging": {
  "LogType": "Csv",        // One of: "Csv", "Json", "Both"
  "FilePathPattern": "logs/FileWatchRest_{0:yyyyMMdd_HHmmss}",
  "LogLevel": "Information",
  "RetainedFileCountLimit": 14
}
```

Notes:

- `LogType` selects which file formats the built-in provider writes. The provider automatically appends the appropriate extension (`.csv` for Csv, `.ndjson` for Json).
- `LogLevel` can be adjusted at runtime via the configuration file; note that changing the log file target (FilePathPattern or LogType) typically requires a service restart for the file provider to open new files.

Troubleshooting  
---------------  

Service Won't Start  
-------------------  

- Run the executable directly from command prompt to see console errors
- Check Windows Event Log for startup failures
- Verify configuration file exists and is valid JSON

Files Not Being Detected  
------------------------  

- Check that folder paths in configuration exist and are accessible
- Verify file extensions match `AllowedExtensions` if specified
- Review logs (JSON/CSV) for watcher errors or restart attempts
- **Note**: Files in folders matching the `ProcessedFolder` configuration value are automatically ignored to prevent infinite processing loops

API Calls Failing  
-----------------  

- Verify `ApiEndpoint` is correct and accessible
- Check `BearerToken` if API requires authentication
- Review retry settings in `appsettings.json`
- Check logs (JSON/CSV) for HTTP status codes

Native AOT Deployment  
----------------------  

For high-performance deployment with Native AOT:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

**Requirements**: Visual C++ build tools must be installed on the build machine for Native AOT compilation.  

Configuration Management  
-------------------------  

- **Single Configuration File**: All settings are now in one place - `FileWatchRest.json`
- Configuration changes are detected automatically - no service restart required
- Invalid JSON will cause service to use previous valid configuration
- Default configuration is created automatically on first run
- Configuration file can be edited manually or through automated deployment scripts
- No need for separate `appsettings.json` modifications

Coding standards & developer tooling
----------------------------------

To keep imports consistent across the repository we enforce "global using" only. All C# using directives must be declared in `FileWatchRest/GlobalUsings.cs`. File-level `using` directives are not allowed and will fail the build.

To ensure developers get fast feedback locally and in CI:

- A pre-commit hook is provided under `githooks/pre-commit` that runs `dotnet restore`, `dotnet build`, and `dotnet test`. To install the hooks locally run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass .\tools\install-git-hooks.ps1
```

- CI runs on GitHub Actions (see `.github/workflows/ci.yml`) and performs restore, build, tests and a format check. The CI will also fail if any file-level using directives are present (via the repository `Directory.Build.targets` enforcement).

If a build error points out a file-level using, move that using to `FileWatchRest/GlobalUsings.cs` and re-run the build or format tools.

---

FileWatchRest - Modern file watching service with REST API integration
------------------------------------------------------------------

What changed in this branch
-------------------------

- File watching and restart logic has been moved into a dedicated FileWatcherManager for clearer lifecycle and restart handling.
- HTTP resilience (retries, backoff, optional circuit breaker) has been extracted into an internal HttpResilienceService — no third-party runtime deps were added.
- Runtime configuration is exposed via an IOptionsMonitor-style wrapper that watches the configuration file and notifies subscribers on changes.
- High-performance structured logging sites were converted to use Microsoft.Extensions.Logging.LoggerMessage (source-defined delegates) to reduce allocations on hot paths.

These refactors improve separation of concerns and make the service safer to run at scale; the configuration model and on-disk JSON remain the single source of truth.

Logging (finalized)
-------------------

The built-in file logging provider is configured via the `Logging` section of the external configuration (`FileWatchRest.json`). Key fields:

- `LogType` ("Csv" | "Json" | "Both") — selects which provider(s) write logs.
- `FilePathPattern` (string) — single filename or pattern the provider uses; the provider appends `.csv` or `.ndjson` as appropriate.
- `LogLevel` (string) — canonical logging level used by the service. Use values: Trace, Debug, Information, Warning, Error, Critical, None.
- `RetainedFileCountLimit` (int) — number of historical log files to keep (older files are pruned).
- Legacy compatibility: `UseJsonFile`/`UseCsvFile` and `JsonFilePath`/`CsvFilePath` remain supported for existing configs but `LogType` + `FilePathPattern` is the preferred shape.

Notes and best practices
--------------------
