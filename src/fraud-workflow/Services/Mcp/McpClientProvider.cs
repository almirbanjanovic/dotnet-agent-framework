using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using System.IO;

namespace Contoso.FraudWorkflow.Services.Mcp;

// Lazy, thread-safe MCP client cache. The MCP HTTP transport opens an
// SSE-style connection on first use, so we want one client per backend
// for the lifetime of the agent process. Failures null the cached client
// so the next call retries (rather than poisoning the cache).
//
// NOTE: This class is intentionally duplicated in src/crm-agent and
// src/product-agent. The component-independence rule forbids a shared
// project; identical code is preferred over coupling.

internal abstract class McpClientProvider : IAsyncDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _baseUrl;
    private McpClient? _client;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    protected McpClientProvider(
        string name,
        string baseUrl,
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory)
    {
        Name = name;
        _baseUrl = baseUrl;
        _httpClientFactory = httpClientFactory;
        _loggerFactory = loggerFactory;
    }

    public string Name { get; }

    public async Task<T> ExecuteWithClientRetryAsync<T>(
        Func<McpClient, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (true)
        {
            attempt++;
            var client = await GetClientAsync(cancellationToken);
            try
            {
                return await operation(client, cancellationToken);
            }
            catch (Exception ex) when (attempt == 1 && IsRecoverableTransportException(ex) && !cancellationToken.IsCancellationRequested)
            {
                await InvalidateClientAsync(client);
            }
        }
    }

    public async Task ExecuteWithClientRetryAsync(
        Func<McpClient, CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        var wrappedOperation = new Func<McpClient, CancellationToken, Task<object?>>(async (client, ct) =>
        {
            await operation(client, ct);
            return null;
        });

        _ = await ExecuteWithClientRetryAsync(
            wrappedOperation,
            cancellationToken);
    }

    public async Task<T> ExecuteWithClientRetryAsync<T>(
        Func<McpClient, CancellationToken, ValueTask<T>> operation,
        CancellationToken cancellationToken)
    {
        var wrappedOperation = new Func<McpClient, CancellationToken, Task<T>>(
            (client, ct) => operation(client, ct).AsTask());

        return await ExecuteWithClientRetryAsync(
            wrappedOperation,
            cancellationToken);
    }

    public async Task ExecuteWithClientRetryAsync(
        Func<McpClient, CancellationToken, ValueTask> operation,
        CancellationToken cancellationToken)
    {
        await ExecuteWithClientRetryAsync(
            (client, ct) => operation(client, ct).AsTask(),
            cancellationToken);
    }

    public async Task<McpClient> GetClientAsync(CancellationToken cancellationToken)
    {
        if (_client is not null)
        {
            return _client;
        }

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            _client ??= await CreateClientAsync(cancellationToken);
            return _client;
        }
        catch
        {
            _client = null;
            throw;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    protected virtual async Task<McpClient> CreateClientAsync(CancellationToken cancellationToken)
    {
        var httpClient = _httpClientFactory.CreateClient(Name);

        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(_baseUrl),
                Name = Name
            },
            httpClient,
            _loggerFactory,
            ownsHttpClient: false);

        return await McpClient.CreateAsync(transport, loggerFactory: _loggerFactory, cancellationToken: cancellationToken);
    }

    protected virtual async ValueTask DisposeClientAsync(McpClient client)
    {
        if (client is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
    }

    public async ValueTask InvalidateClientAsync(McpClient? expectedClient = null)
    {
        await _semaphore.WaitAsync();
        try
        {
            if (_client is null)
            {
                return;
            }

            if (expectedClient is not null && !ReferenceEquals(_client, expectedClient))
            {
                return;
            }
            // Detach the cached instance so the next caller creates a fresh
            // client. Do not dispose here: providers are singletons, and
            // concurrent requests may still hold references to the current
            // client while this invalidation runs.
            _client = null;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    protected virtual bool IsRecoverableTransportException(Exception ex)
    {
        if (ex is OperationCanceledException)
        {
            return false;
        }

        return ex is IOException || ex is HttpRequestException || ex.InnerException is IOException;
    }

    public async ValueTask DisposeAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            if (_client is not null)
            {
                await DisposeClientAsync(_client);
                _client = null;
            }
        }
        finally
        {
            _semaphore.Release();
            _semaphore.Dispose();
        }
    }
}
