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
  "BearerToken": "your-bearer-token-here",
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
  "LogType": "Csv",
  "FilePathPattern": "logs/FileWatchRest_{0:yyyyMMdd_HHmmss}",
  "LogLevel": "Information",
  "RetainedFileCountLimit": 14
}
```

Notes:

- The service prefers configuration from `$env:ProgramData\\FileWatchRest\\FileWatchRest.json` (this is the single source of configuration). If that file does not exist the embedded defaults or `appsettings.json` fallback values are used.
- CSV formatting is intentionally conservative and safe for downstream ingestion. If you need a different CSV schema, replace or extend `Logging/SimpleFileLoggerProvider.cs`.
- For production deployments we recommend JSON logs for machine ingestion (ELK, Seq, Datadog). Enable `LogType: "Both"` if you want both CSV and JSON outputs.

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

---

FileWatchRest - Modern file watching service with REST API integration
------------------------------------------------------------------
