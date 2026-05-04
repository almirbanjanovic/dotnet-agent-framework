namespace Contoso.OrchestratorAgent.Services;

// Strongly-typed wrappers around the specialist agents' HttpClients. The
// only purpose of having two distinct types is to let the DI container
// (and AddHttpClient<T>) attach a different base URL and resilience
// pipeline to each.

internal sealed class CrmAgentClient
{
    public CrmAgentClient(HttpClient httpClient)
    {
        HttpClient = httpClient;
    }

    public HttpClient HttpClient { get; }
}

internal sealed class ProductAgentClient
{
    public ProductAgentClient(HttpClient httpClient)
    {
        HttpClient = httpClient;
    }

    public HttpClient HttpClient { get; }
}
