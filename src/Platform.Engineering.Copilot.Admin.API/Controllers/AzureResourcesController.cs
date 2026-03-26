using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Platform.Engineering.Copilot.Admin.API.Services;

namespace Platform.Engineering.Copilot.Admin.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AzureResourcesController : ControllerBase
{
    private readonly ILogger<AzureResourcesController> _logger;
    private readonly IAzureAuthService _azureAuth;
    private static readonly JsonSerializerOptions _jsonOpts = new() { PropertyNameCaseInsensitive = true };

    public AzureResourcesController(ILogger<AzureResourcesController> logger, IAzureAuthService azureAuth)
    {
        _logger = logger;
        _azureAuth = azureAuth;
    }

    /// <summary>
    /// Get Azure resource overview: tenant, subscription, management groups, policies, resource groups, Entra ID info.
    /// </summary>
    [HttpGet("overview")]
    [ProducesResponseType(typeof(AzureResourceOverviewDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AzureResourceOverviewDto>> GetOverview()
    {
        var settings = IntegrationsController.LoadSettings();
        var azureSettings = settings.Azure;

        if (azureSettings == null || string.IsNullOrWhiteSpace(azureSettings.TenantId))
        {
            return Ok(new AzureResourceOverviewDto
            {
                Status = "not_configured",
                Message = "Azure integration is not configured. Go to Settings > Integrations to set up Azure."
            });
        }

        var overview = new AzureResourceOverviewDto
        {
            Status = "configured",
            TenantInfo = new TenantInfoDto
            {
                TenantId = azureSettings.TenantId ?? "",
                CloudEnvironment = azureSettings.CloudEnvironment ?? "AzureGovernment",
                AuthMethod = azureSettings.AuthMethod ?? "credentials"
            },
            SubscriptionInfo = new SubscriptionInfoDto
            {
                SubscriptionId = azureSettings.SubscriptionId ?? ""
            }
        };

        // Determine ARM endpoint based on cloud environment
        var armEndpoint = _azureAuth.GetArmEndpoint(azureSettings.CloudEnvironment);

        // Get auth token
        string? accessToken;
        try
        {
            accessToken = await _azureAuth.GetAccessTokenAsync(azureSettings, armEndpoint);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to authenticate with Azure");
            overview.Message = $"Authentication failed: {ex.Message}";
            return Ok(overview);
        }

        if (string.IsNullOrEmpty(accessToken))
        {
            overview.Message = "Could not obtain Azure access token. Check your credentials in Settings > Integrations.";
            return Ok(overview);
        }

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var subscriptionId = azureSettings.SubscriptionId;

        // Fetch subscription details
        await FetchSubscriptionDetails(httpClient, armEndpoint, subscriptionId, overview);

        // Fetch resource groups, management groups, policies in parallel
        var rgTask = FetchResourceGroups(httpClient, armEndpoint, subscriptionId, overview);
        var mgTask = FetchManagementGroups(httpClient, armEndpoint, overview);
        var polTask = FetchPolicies(httpClient, armEndpoint, subscriptionId, overview);

        await Task.WhenAll(rgTask, mgTask, polTask);

        overview.Status = overview.ResourceGroups.Count > 0 || !string.IsNullOrEmpty(overview.SubscriptionInfo.DisplayName)
            ? "connected" : "configured";

        return Ok(overview);
    }



    private async Task FetchSubscriptionDetails(HttpClient httpClient, string armEndpoint, string subscriptionId, AzureResourceOverviewDto overview)
    {
        try
        {
            var url = $"{armEndpoint}/subscriptions/{subscriptionId}?api-version=2022-12-01";
            var response = await httpClient.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                overview.SubscriptionInfo.DisplayName = doc.RootElement.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? "" : "";
                overview.SubscriptionInfo.State = doc.RootElement.TryGetProperty("state", out var st) ? st.GetString() ?? "" : "";
            }
            else
            {
                _logger.LogWarning("Failed to fetch subscription details: {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not fetch subscription details");
        }
    }

    private async Task FetchResourceGroups(HttpClient httpClient, string armEndpoint, string subscriptionId, AzureResourceOverviewDto overview)
    {
        try
        {
            var url = $"{armEndpoint}/subscriptions/{subscriptionId}/resourcegroups?api-version=2021-04-01";
            var response = await httpClient.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("value", out var rgArray))
                {
                    foreach (var rg in rgArray.EnumerateArray())
                    {
                        overview.ResourceGroups.Add(new ResourceGroupDto
                        {
                            Name = rg.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                            Location = rg.TryGetProperty("location", out var l) ? l.GetString() ?? "" : "",
                            ProvisioningState = rg.TryGetProperty("properties", out var props)
                                ? (props.TryGetProperty("provisioningState", out var ps) ? ps.GetString() ?? "" : "")
                                : ""
                        });
                    }
                }
            }
            else
            {
                _logger.LogWarning("Failed to fetch resource groups: {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not fetch resource groups");
        }
    }

    private async Task FetchManagementGroups(HttpClient httpClient, string armEndpoint, AzureResourceOverviewDto overview)
    {
        try
        {
            var url = $"{armEndpoint}/providers/Microsoft.Management/managementGroups?api-version=2021-04-01";
            var response = await httpClient.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("value", out var mgArray))
                {
                    foreach (var mg in mgArray.EnumerateArray())
                    {
                        overview.ManagementGroups.Add(new ManagementGroupDto
                        {
                            Id = mg.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                            Name = mg.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                            DisplayName = mg.TryGetProperty("properties", out var props)
                                ? (props.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? "" : "")
                                : ""
                        });
                    }
                }
            }
            else
            {
                _logger.LogWarning("Failed to fetch management groups: {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not fetch management groups");
        }
    }

    private async Task FetchPolicies(HttpClient httpClient, string armEndpoint, string subscriptionId, AzureResourceOverviewDto overview)
    {
        try
        {
            var url = $"{armEndpoint}/subscriptions/{subscriptionId}/providers/Microsoft.Authorization/policyAssignments?api-version=2022-06-01";
            var response = await httpClient.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("value", out var polArray))
                {
                    foreach (var pol in polArray.EnumerateArray())
                    {
                        var props = pol.TryGetProperty("properties", out var p) ? p : default;
                        overview.Policies.Add(new PolicyAssignmentDto
                        {
                            Id = pol.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                            Name = pol.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                            DisplayName = props.ValueKind != JsonValueKind.Undefined && props.TryGetProperty("displayName", out var dn)
                                ? dn.GetString() ?? "" : "",
                            EnforcementMode = props.ValueKind != JsonValueKind.Undefined && props.TryGetProperty("enforcementMode", out var em)
                                ? em.GetString() ?? "" : ""
                        });
                    }
                }
            }
            else
            {
                _logger.LogWarning("Failed to fetch policies: {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not fetch policies");
        }
    }
}

#region DTOs

public class AzureResourceOverviewDto
{
    public string Status { get; set; } = "not_configured";
    public string? Message { get; set; }
    public TenantInfoDto TenantInfo { get; set; } = new();
    public SubscriptionInfoDto SubscriptionInfo { get; set; } = new();
    public List<ManagementGroupDto> ManagementGroups { get; set; } = new();
    public List<PolicyAssignmentDto> Policies { get; set; } = new();
    public List<ResourceGroupDto> ResourceGroups { get; set; } = new();
    public EntraIdInfoDto EntraIdInfo { get; set; } = new();
}

public class TenantInfoDto
{
    public string TenantId { get; set; } = "";
    public string CloudEnvironment { get; set; } = "";
    public string AuthMethod { get; set; } = "";
    public string? TenantDisplayName { get; set; }
}

public class SubscriptionInfoDto
{
    public string SubscriptionId { get; set; } = "";
    public string? DisplayName { get; set; }
    public string? State { get; set; }
}

public class ManagementGroupDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
}

public class PolicyAssignmentDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string EnforcementMode { get; set; } = "";
}

public class ResourceGroupDto
{
    public string Name { get; set; } = "";
    public string Location { get; set; } = "";
    public string ProvisioningState { get; set; } = "";
}

public class EntraIdInfoDto
{
    public string? TenantDisplayName { get; set; }
    public int? UserCount { get; set; }
    public int? GroupCount { get; set; }
    public int? AppRegistrationCount { get; set; }
}

#endregion
