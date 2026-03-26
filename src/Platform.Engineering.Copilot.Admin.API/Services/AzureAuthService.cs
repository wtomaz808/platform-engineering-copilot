using Azure.Core;
using Azure.Identity;
using Platform.Engineering.Copilot.Admin.API.Controllers;

namespace Platform.Engineering.Copilot.Admin.API.Services;

public interface IAzureAuthService
{
    /// <summary>
    /// Get an Azure access token using the current integration settings.
    /// </summary>
    Task<string> GetAccessTokenAsync(AzureIntegrationSettingsDto azureSettings, string armEndpoint);

    /// <summary>
    /// Get the ARM endpoint URL for the configured cloud environment.
    /// </summary>
    string GetArmEndpoint(string cloudEnvironment);
}

public class AzureAuthService : IAzureAuthService
{
    private readonly ILogger<AzureAuthService> _logger;

    public AzureAuthService(ILogger<AzureAuthService> logger)
    {
        _logger = logger;
    }

    public string GetArmEndpoint(string cloudEnvironment)
    {
        return (cloudEnvironment ?? "AzureGovernment") == "AzureGovernment"
            ? "https://management.usgovcloudapi.net"
            : "https://management.azure.com";
    }

    public async Task<string> GetAccessTokenAsync(AzureIntegrationSettingsDto azureSettings, string armEndpoint)
    {
        var authorityHost = (azureSettings.CloudEnvironment ?? "AzureGovernment") == "AzureGovernment"
            ? AzureAuthorityHosts.AzureGovernment
            : AzureAuthorityHosts.AzurePublicCloud;

        TokenCredential credential;

        switch (azureSettings.AuthMethod)
        {
            case "servicePrincipal":
                if (string.IsNullOrWhiteSpace(azureSettings.ClientId) || string.IsNullOrWhiteSpace(azureSettings.ClientSecret))
                    throw new InvalidOperationException("Service Principal requires Client ID and Client Secret.");
                credential = new ClientSecretCredential(
                    azureSettings.TenantId,
                    azureSettings.ClientId,
                    azureSettings.ClientSecret,
                    new ClientSecretCredentialOptions { AuthorityHost = authorityHost });
                break;

            case "credentials":
                if (string.IsNullOrWhiteSpace(azureSettings.Username) || string.IsNullOrWhiteSpace(azureSettings.Password))
                    throw new InvalidOperationException("Username/Password credentials are required.");
                credential = new UsernamePasswordCredential(
                    azureSettings.Username,
                    azureSettings.Password,
                    azureSettings.TenantId,
                    azureSettings.ClientId,
                    new UsernamePasswordCredentialOptions { AuthorityHost = authorityHost });
                break;

            case "managedIdentity":
                credential = new ManagedIdentityCredential();
                break;

            default:
                throw new InvalidOperationException($"Unsupported auth method: {azureSettings.AuthMethod}");
        }

        var tokenRequestContext = new TokenRequestContext(new[] { $"{armEndpoint}/.default" });
        var token = await credential.GetTokenAsync(tokenRequestContext, CancellationToken.None);

        _logger.LogDebug("Acquired Azure token for {AuthMethod}", azureSettings.AuthMethod);
        return token.Token;
    }
}
