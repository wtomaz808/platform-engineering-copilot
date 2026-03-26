using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Platform.Engineering.Copilot.Admin.API.DTOs;
using Platform.Engineering.Copilot.Admin.API.Services;

namespace Platform.Engineering.Copilot.Admin.API.Controllers;

/// <summary>
/// API controller for Azure drift detection.
/// Uses Azure Resource Graph + Policy compliance APIs to detect configuration drift.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class DriftController : ControllerBase
{
    private readonly ILogger<DriftController> _logger;
    private readonly IAzureAuthService _azureAuth;
    private static readonly JsonSerializerOptions _jsonOpts = new() { PropertyNameCaseInsensitive = true };

    // Cache scan results (5-minute TTL)
    private static DriftScanResponseDto? _cachedScan;
    private static DateTime _lastScanTime = DateTime.MinValue;

    public DriftController(ILogger<DriftController> logger, IAzureAuthService azureAuth)
    {
        _logger = logger;
        _azureAuth = azureAuth;
    }

    /// <summary>
    /// Scan for configuration drift using Azure Resource Graph and Policy compliance.
    /// Compares actual resource configurations against policy assignment baselines.
    /// </summary>
    [HttpGet("scan")]
    [ProducesResponseType(typeof(DriftScanResponseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<DriftScanResponseDto>> ScanForDrift([FromQuery] bool forceRefresh = false)
    {
        var settings = IntegrationsController.LoadSettings();
        var azureSettings = settings.Azure;

        if (azureSettings == null || string.IsNullOrWhiteSpace(azureSettings.SubscriptionId))
        {
            return Ok(new DriftScanResponseDto
            {
                Status = "not_configured",
                Message = "Azure integration is not configured. Go to Settings > Integrations to set up Azure."
            });
        }

        // Return cached if scanned in last 5 minutes (unless forced)
        if (!forceRefresh && _cachedScan != null && (DateTime.UtcNow - _lastScanTime).TotalMinutes < 5)
        {
            return Ok(_cachedScan);
        }

        var armEndpoint = _azureAuth.GetArmEndpoint(azureSettings.CloudEnvironment);
        string accessToken;
        try
        {
            accessToken = await _azureAuth.GetAccessTokenAsync(azureSettings, armEndpoint);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to acquire Azure token for drift detection");
            return Ok(new DriftScanResponseDto
            {
                Status = "auth_error",
                Message = $"Authentication failed: {ex.Message}"
            });
        }

        var subscriptionId = azureSettings.SubscriptionId!;
        var response = new DriftScanResponseDto
        {
            Status = "ok",
            SubscriptionId = subscriptionId
        };

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        // Fetch drift data in parallel: policy compliance + resource graph inventory
        var policyTask = FetchNonCompliantPolicyStates(httpClient, armEndpoint, subscriptionId);
        var resourceCountTask = FetchResourceCount(httpClient, armEndpoint, subscriptionId);

        await Task.WhenAll(policyTask, resourceCountTask);

        var policyDriftItems = policyTask.Result;
        var totalResources = resourceCountTask.Result;

        response.DriftItems = policyDriftItems;
        response.TotalResources = totalResources;
        response.DriftedResources = policyDriftItems.Count;
        response.CompliantResources = Math.Max(0, totalResources - policyDriftItems.Count);
        response.ScannedAt = DateTime.UtcNow;

        _cachedScan = response;
        _lastScanTime = DateTime.UtcNow;

        _logger.LogInformation("Drift scan complete: {Drifted}/{Total} resources with drift",
            response.DriftedResources, response.TotalResources);

        return Ok(response);
    }

    #region Azure REST API Calls

    /// <summary>
    /// Fetch non-compliant policy states via Azure Policy Insights REST API.
    /// This is the primary source for configuration drift — it compares live resources against policy baselines.
    /// </summary>
    private async Task<List<DriftedResourceDto>> FetchNonCompliantPolicyStates(
        HttpClient httpClient, string armEndpoint, string subscriptionId)
    {
        var driftItems = new List<DriftedResourceDto>();

        try
        {
            // Azure Policy Insights API — query non-compliant resources
            var url = $"{armEndpoint}/subscriptions/{subscriptionId}/providers/Microsoft.PolicyInsights/policyStates/latest/queryResults"
                + "?api-version=2019-10-01"
                + "&$filter=complianceState eq 'NonCompliant'"
                + "&$top=200"
                + "&$orderby=timestamp desc";

            var httpResponse = await httpClient.PostAsync(url, new StringContent("{}", Encoding.UTF8, "application/json"));

            if (!httpResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Policy Insights API returned {StatusCode}", httpResponse.StatusCode);
                return driftItems;
            }

            var json = await httpResponse.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("value", out var values))
            {
                foreach (var item in values.EnumerateArray())
                {
                    var resourceId = GetJsonString(item, "resourceId");
                    var policyDefId = GetJsonString(item, "policyDefinitionId");
                    var policyDefName = GetJsonString(item, "policyDefinitionName");
                    var policyAssignName = GetJsonString(item, "policyAssignmentName");

                    // Determine severity from policy effect
                    var effect = GetJsonString(item, "policyDefinitionAction")?.ToLowerInvariant();
                    var severity = effect switch
                    {
                        "deny" => "Critical",
                        "audit" => "Warning",
                        "auditifnotexists" => "Warning",
                        "deployifnotexists" => "Warning",
                        _ => "Info"
                    };

                    driftItems.Add(new DriftedResourceDto
                    {
                        ResourceId = resourceId,
                        ResourceName = ExtractResourceName(resourceId),
                        ResourceType = GetJsonString(item, "resourceType"),
                        ResourceGroup = GetJsonString(item, "resourceGroup"),
                        DriftType = "PolicyViolation",
                        Severity = severity,
                        PropertyPath = "policy",
                        ExpectedValue = "Compliant",
                        ActualValue = "NonCompliant",
                        PolicyName = !string.IsNullOrEmpty(policyAssignName) ? policyAssignName : policyDefName,
                        PolicyDefinitionId = policyDefId
                    });
                }
            }

            _logger.LogInformation("Found {Count} non-compliant policy states", driftItems.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch policy compliance states");
        }

        return driftItems;
    }

    /// <summary>
    /// Get total resource count via Azure Resource Graph REST API.
    /// </summary>
    private async Task<int> FetchResourceCount(HttpClient httpClient, string armEndpoint, string subscriptionId)
    {
        try
        {
            var url = $"{armEndpoint}/providers/Microsoft.ResourceGraph/resources?api-version=2021-03-01";

            var requestBody = new
            {
                subscriptions = new[] { subscriptionId },
                query = "resources | summarize count()"
            };

            var content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");

            var httpResponse = await httpClient.PostAsync(url, content);

            if (!httpResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Resource Graph API returned {StatusCode}", httpResponse.StatusCode);
                return 0;
            }

            var json = await httpResponse.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("data", out var data) &&
                data.ValueKind == JsonValueKind.Array &&
                data.GetArrayLength() > 0)
            {
                var row = data[0];
                if (row.TryGetProperty("count_", out var countProp))
                    return countProp.GetInt32();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch resource count");
        }

        return 0;
    }

    #endregion

    #region Helpers

    private static string GetJsonString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString() ?? string.Empty
            : string.Empty;
    }

    private static string ExtractResourceName(string resourceId)
    {
        if (string.IsNullOrEmpty(resourceId)) return "Unknown";
        var parts = resourceId.Split('/');
        return parts.Length > 0 ? parts[^1] : "Unknown";
    }

    #endregion
}
