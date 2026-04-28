using Azure.Identity;
using FluentAssertions;
using simple_agent;

namespace Contoso.SimpleAgent.Tests;

public class AuthContextTests
{
    [Fact]
    public void ApiKeyPresent_UsesAzureKeyCredential()
    {
        var context = SimpleAgentAuth.CreateAuthContext("api-key", "tenant-1");

        context.UseApiKey.Should().BeTrue();
        context.ApiKeyCredential.Should().NotBeNull();
        context.TokenCredential.Should().BeNull();
    }

    [Fact]
    public void ApiKeyAbsent_UsesDefaultAzureCredential()
    {
        var context = SimpleAgentAuth.CreateAuthContext(null, "tenant-1");

        context.UseApiKey.Should().BeFalse();
        context.ApiKeyCredential.Should().BeNull();
        context.TokenCredential.Should().BeOfType<DefaultAzureCredential>();
    }
}
