using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using NSubstitute;

namespace Contoso.AgentCommon.Tests;

public sealed class McpClientProviderTests
{
    [Fact]
    public async Task GetClientAsync_SuccessfulConnection_IsCached()
    {
        var client = Substitute.For<McpClient>();
        var provider = new TestMcpClientProvider(_ => Task.FromResult(client));

        var first = await provider.GetClientAsync(CancellationToken.None);
        var second = await provider.GetClientAsync(CancellationToken.None);

        first.Should().BeSameAs(second);
        provider.CreateCallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetClientAsync_FailureNullsClientForRetry()
    {
        var client = Substitute.For<McpClient>();
        var callCount = 0;
        var provider = new TestMcpClientProvider(_ =>
        {
            if (callCount++ == 0)
            {
                throw new InvalidOperationException("boom");
            }

            return Task.FromResult(client);
        });

        var act = () => provider.GetClientAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
        var retry = await provider.GetClientAsync(CancellationToken.None);

        retry.Should().BeSameAs(client);
        provider.CreateCallCount.Should().Be(2);
    }

    [Fact]
    public async Task GetClientAsync_PassesCancellationToken()
    {
        var client = Substitute.For<McpClient>();
        using var source = new CancellationTokenSource();
        var provider = new TestMcpClientProvider(_ => Task.FromResult(client));

        _ = await provider.GetClientAsync(source.Token);

        provider.LastToken.Should().Be(source.Token);
    }

    [Fact]
    public async Task GetClientAsync_ConcurrentInitialization_IsSafe()
    {
        var client = Substitute.For<McpClient>();
        var tcs = new TaskCompletionSource<McpClient>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new TestMcpClientProvider(_ => tcs.Task);

        var firstTask = provider.GetClientAsync(CancellationToken.None);
        var secondTask = provider.GetClientAsync(CancellationToken.None);

        tcs.SetResult(client);
        var results = await Task.WhenAll(firstTask, secondTask);

        results[0].Should().BeSameAs(results[1]);
        provider.CreateCallCount.Should().Be(1);
    }

    [Fact]
    public async Task DisposeAsync_DisposesClient()
    {
        var client = Substitute.For<McpClient>();
        var provider = new TestMcpClientProvider(_ => Task.FromResult(client));

        _ = await provider.GetClientAsync(CancellationToken.None);
        await provider.DisposeAsync();

        provider.DisposedClient.Should().BeSameAs(client);
    }

    private sealed class TestMcpClientProvider : McpClientProvider
    {
        private readonly Func<CancellationToken, Task<McpClient>> _factory;

        public TestMcpClientProvider(Func<CancellationToken, Task<McpClient>> factory)
            : base("test-mcp", "http://localhost", NullLoggerFactory.Instance)
        {
            _factory = factory;
        }

        public int CreateCallCount { get; private set; }

        public CancellationToken LastToken { get; set; }

        public McpClient? DisposedClient { get; private set; }

        protected override async Task<McpClient> CreateClientAsync(CancellationToken cancellationToken)
        {
            CreateCallCount++;
            LastToken = cancellationToken;
            return await _factory(cancellationToken);
        }

        protected override ValueTask DisposeClientAsync(McpClient client)
        {
            DisposedClient = client;
            return ValueTask.CompletedTask;
        }
    }
}
