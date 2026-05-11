using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

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

    public async ValueTask DisposeAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            if (_client is not null)
            {
                if (_client is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync();
                }
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
