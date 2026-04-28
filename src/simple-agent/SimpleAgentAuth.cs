using Azure;
using Azure.Core;
using Azure.Identity;

namespace simple_agent;

internal static class SimpleAgentAuth
{
    internal static AuthContext CreateAuthContext(string? apiKey, string? tenantId)
    {
        if (!string.IsNullOrEmpty(apiKey))
        {
            return AuthContext.ForApiKey(new AzureKeyCredential(apiKey));
        }

        var credential = string.IsNullOrEmpty(tenantId)
            ? new DefaultAzureCredential()
            : new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = tenantId });

        return AuthContext.ForTokenCredential(credential);
    }
}

internal sealed record AuthContext(bool UseApiKey, AzureKeyCredential? ApiKeyCredential, TokenCredential? TokenCredential)
{
    internal static AuthContext ForApiKey(AzureKeyCredential credential) => new(true, credential, null);

    internal static AuthContext ForTokenCredential(TokenCredential credential) => new(false, null, credential);
}
