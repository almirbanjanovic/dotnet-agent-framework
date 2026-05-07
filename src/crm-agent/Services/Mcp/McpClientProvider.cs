using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

// Lazy, thread-safe MCP client cache. The MCP HTTP transport opens an
// SSE-style connection on first use, so we want one client per backend
// for the lifetime of the agent process. Failures null the cached client
// so the next call retries (rather than poisoning the cache).
//
// Subclasses (`CrmMcpClientProvider`, `KnowledgeMcpClientProvider`) only
// supply the backend's friendly name and base URL. The friendly name is
// also the IHttpClientFactory client name, so the named handler chain
// (which includes `CustomerHeaderForwarder` to propagate the inbound
// `X-Customer-Entra-Id` header to the downstream MCP server) is wired in
// for free.
//
// NOTE: This class is duplicated (intentionally) in src/product-agent.
// The component-independence rule forbids a shared project; identical
// code is preferred over coupling.

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
        // Resolve a named HttpClient so the `CustomerHeaderForwarder`
        // DelegatingHandler attached to that name in DI runs for every
        // outbound MCP request. We deliberately do NOT set BaseAddress on
        // the HttpClient — the transport sends absolute URIs anchored at
        // `Endpoint`, and a stale BaseAddress would shadow it.
        //
        // ownsHttpClient: false — the HttpClient (and its handler chain)
        // are owned by IHttpClientFactory's pool and must NOT be disposed
        // by the transport when the McpClient is disposed.
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
