using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

// Lazy, thread-safe MCP client cache. The MCP HTTP transport opens an
// SSE-style connection on first use, so we want one client per backend
// for the lifetime of the agent process. Failures null the cached client
// so the next call retries (rather than poisoning the cache).
//
// Subclasses (`CrmMcpClientProvider`, `KnowledgeMcpClientProvider`) only
// supply the backend's friendly name and base URL.

internal abstract class McpClientProvider : IAsyncDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly string _baseUrl;
    private McpClient? _client;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    protected McpClientProvider(string name, string baseUrl, ILoggerFactory loggerFactory)
    {
        Name = name;
        _baseUrl = baseUrl;
        _loggerFactory = loggerFactory;
    }

    public string Name { get; }

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
            // Reset the cache so the next caller can retry against a
            // backend that may have come back online.
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
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(_baseUrl),
            Name = Name
        }, _loggerFactory);

        return await McpClient.CreateAsync(transport, loggerFactory: _loggerFactory, cancellationToken: cancellationToken);
    }

    protected virtual async ValueTask DisposeClientAsync(McpClient client)
    {
        if (client is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
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
