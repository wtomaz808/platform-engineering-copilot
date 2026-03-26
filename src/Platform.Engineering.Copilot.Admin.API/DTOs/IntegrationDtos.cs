namespace Platform.Engineering.Copilot.Admin.API.DTOs;

public class IntegrationSettingsDto
{
    public AzureIntegrationSettingsDto Azure { get; set; } = new();
    public GitHubIntegrationSettingsDto GitHub { get; set; } = new();
    public AdoIntegrationSettingsDto AzureDevOps { get; set; } = new();
}

public class AzureIntegrationSettingsDto
{
    public bool Enabled { get; set; }
    public string CloudEnvironment { get; set; } = "AzureGovernment";
    public string TenantId { get; set; } = "";
    public string SubscriptionId { get; set; } = "";
    public string AuthMethod { get; set; } = "servicePrincipal";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public bool UseManagedIdentity { get; set; }
}

public class GitHubIntegrationSettingsDto
{
    public bool Enabled { get; set; }
    public string Organization { get; set; } = "";
    public string Token { get; set; } = "";
    public string ApiBaseUrl { get; set; } = "https://api.github.com";
}

public class AdoIntegrationSettingsDto
{
    public bool Enabled { get; set; }
    public string ServerType { get; set; } = "services";
    public string ServerUrl { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public string DefaultCollection { get; set; } = "DefaultCollection";
    public string Organization { get; set; } = "";
    public string Project { get; set; } = "";
}

public class IntegrationTestResultDto
{
    public bool Success { get; set; }
    public string? Message { get; set; }
}
