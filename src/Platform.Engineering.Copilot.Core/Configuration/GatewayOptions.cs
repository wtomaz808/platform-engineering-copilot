namespace Platform.Engineering.Copilot.Core.Configuration;

/// <summary>
/// Configuration options for the gateway
/// </summary>
public class GatewayOptions
{
    public const string SectionName = "Gateway";

    /// <summary>
    /// Azure configuration
    /// </summary>
    public AzureGatewayOptions Azure { get; set; } = new();

    /// <summary>
    /// GitHub configuration
    /// </summary>
    public GitHubGatewayOptions GitHub { get; set; } = new();

    /// <summary>
    /// Azure DevOps configuration
    /// </summary>
    public AzureDevOpsGatewayOptions AzureDevOps { get; set; } = new();

    /// <summary>
    /// Connection timeout in seconds
    /// </summary>
    public int ConnectionTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Request timeout in seconds
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 120;
}

/// <summary>
/// Azure gateway configuration
/// </summary>
public class AzureGatewayOptions
{
    public const string SectionName = "Gateway:Azure";

    /// <summary>
    /// Default Azure subscription ID to use for resource operations
    /// </summary>
    public string? SubscriptionId { get; set; }

    /// <summary>
    /// Azure tenant ID
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// Authentication method: "credentials" (username/password), "servicePrincipal", or "managedIdentity"
    /// </summary>
    public string AuthMethod { get; set; } = "servicePrincipal";

    /// <summary>
    /// Azure username (UPN) for username/password authentication (e.g. user@tenant.onmicrosoft.us)
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// Azure password for username/password authentication
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Azure client ID (Service Principal Application ID, or public client app ID for ROPC flow)
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Azure client secret (Service Principal secret — only needed for servicePrincipal auth method)
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Azure cloud environment (AzureCloud, AzureGovernment, etc.)
    /// </summary>
    public string CloudEnvironment { get; set; } = "AzureCloud";

    /// <summary>
    /// Whether to use managed identity for authentication
    /// </summary>
    public bool UseManagedIdentity { get; set; }

    /// <summary>
    /// Whether Azure integration is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Enable user token passthrough (client app passes user's Azure AD token)
    /// When true, MCP uses the user's identity instead of service identity
    /// </summary>
    public bool EnableUserTokenPassthrough { get; set; } = false;
}

/// <summary>
/// GitHub gateway configuration (unified for both API and PR operations)
/// </summary>
public class GitHubGatewayOptions
{
    /// <summary>
    /// GitHub personal access token for API authentication
    /// </summary>
    public string? AccessToken { get; set; }

    /// <summary>
    /// GitHub API base URL (e.g., https://api.github.com or https://github.enterprise.com/api/v3)
    /// </summary>
    public string ApiBaseUrl { get; set; } = "https://api.github.com";

    /// <summary>
    /// Default organization/user for repository operations
    /// </summary>
    public string? DefaultOwner { get; set; }

    /// <summary>
    /// Whether GitHub integration is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Webhook secret for securing GitHub webhooks
    /// </summary>
    public string? WebhookSecret { get; set; }

    /// <summary>
    /// Enable pull request review operations
    /// </summary>
    public bool EnablePrReviews { get; set; } = true;

    /// <summary>
    /// Automatically approve pull requests when tests pass
    /// </summary>
    public bool AutoApproveOnSuccess { get; set; } = false;

    /// <summary>
    /// Maximum file size in KB for GitHub operations (default 1MB)
    /// </summary>
    public int MaxFileSizeKb { get; set; } = 1024;
}

/// <summary>
/// Azure DevOps gateway configuration
/// </summary>
public class AzureDevOpsGatewayOptions
{
    /// <summary>
    /// Azure DevOps server URL (e.g. https://dev.azure.us/org or http://adoserver:8080)
    /// </summary>
    public string? ServerUrl { get; set; }

    /// <summary>
    /// Personal Access Token for Azure DevOps authentication
    /// </summary>
    public string? AccessToken { get; set; }

    /// <summary>
    /// Collection name for on-premises Azure DevOps Server (e.g., DefaultCollection)
    /// </summary>
    public string? DefaultCollection { get; set; }

    /// <summary>
    /// Whether Azure DevOps integration is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;
}