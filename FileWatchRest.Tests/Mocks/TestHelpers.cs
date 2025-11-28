namespace FileWatchRest.Tests;

public class TestHttpClientFactory : IHttpClientFactory {
    public HttpClient CreateClient(string name = "") => new();
}

public class TestHostApplicationLifetime : IHostApplicationLifetime, IDisposable {
    private readonly CancellationTokenSource _applicationStartedSource = new();
    private readonly CancellationTokenSource _applicationStoppingSource = new();
    private readonly CancellationTokenSource _applicationStoppedSource = new();

    public CancellationToken ApplicationStarted => _applicationStartedSource.Token;
    public CancellationToken ApplicationStopping => _applicationStoppingSource.Token;
    public CancellationToken ApplicationStopped => _applicationStoppedSource.Token;

    public void StopApplication() {
        _applicationStoppingSource.Cancel();
        _applicationStoppedSource.Cancel();
    }

    public void Dispose() {
        try {
            _applicationStartedSource.Cancel();
            _applicationStoppingSource.Cancel();
            _applicationStoppedSource.Cancel();
        }
        finally {
            _applicationStartedSource.Dispose();
            _applicationStoppingSource.Dispose();
            _applicationStoppedSource.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}

public class HttpClientFactoryMock : IHttpClientFactory {
    public HttpClient CreateClient(string name) => new();
}

public class HostApplicationLifetimeMock : IHostApplicationLifetime {
    public CancellationToken ApplicationStarted => CancellationToken.None;
    public CancellationToken ApplicationStopping => CancellationToken.None;
    public CancellationToken ApplicationStopped => CancellationToken.None;
    public void StopApplication() { }
}

public class ResilienceServiceMock : IResilienceService {
    public Task<ResilienceResult> SendWithRetriesAsync(Func<CancellationToken, Task<HttpRequestMessage>> requestFactory, HttpClient client, string endpointKey, ExternalConfiguration config, CancellationToken ct) =>
        Task.FromResult(new ResilienceResult(true, 200, null, null, 0, false));
}
