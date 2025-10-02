// Centralized global usings to reduce per-file boilerplate and avoid unnecessary/useless imports
// Add only broadly used namespaces here. Keep the list conservative to avoid surprising name collisions.

global using System.Collections.Concurrent;
global using System.Text;
global using System.Text.Json;
global using System.Text.Json.Serialization;
global using System.Net;
global using System.Net.Http.Headers;
global using System.Threading.Channels;
global using System.Text.RegularExpressions;
global using System.Diagnostics.Metrics;
global using System.Security.Cryptography;
global using FileWatchRest.Configuration;
global using FileWatchRest.Models;
global using FileWatchRest.Logging;
global using FileWatchRest.Services;
global using Microsoft.Extensions.Options;
global using System.Runtime.CompilerServices;
global using System.Diagnostics.CodeAnalysis;
