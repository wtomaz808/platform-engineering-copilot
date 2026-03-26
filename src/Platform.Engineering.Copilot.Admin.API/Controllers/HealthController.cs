using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Platform.Engineering.Copilot.Admin.API.DTOs;
using Platform.Engineering.Copilot.Admin.API.Services;

namespace Platform.Engineering.Copilot.Admin.API.Controllers;

/// <summary>
/// API controller for Azure Resource Health and Service Health.
/// Queries real Azure ARM APIs using integration settings.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class HealthController : ControllerBase
{
    private readonly ILogger<HealthController> _logger;
    private readonly IAzureAuthService _azureAuth;
    private static readonly JsonSerializerOptions _jsonOpts = new() { PropertyNameCaseInsensitive = true };

    public HealthController(ILogger<HealthController> logger, IAzureAuthService azureAuth)
    {
        _logger = logger;
        _azureAuth = azureAuth;
    }

    /// <summary>
    /// Get resource health statuses for all resources in the configured subscription.
    /// Calls Azure Resource Health API: /subscriptions/{subId}/providers/Microsoft.ResourceHealth/availabilityStatuses
    /// </summary>
    [HttpGet("resources")]
    [ProducesResponseType(typeof(ResourceHealthResponseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ResourceHealthResponseDto>> GetResourceHealth()
    {
        var settings = IntegrationsController.LoadSettings();
        var azureSettings = settings.Azure;

        if (azureSettings == null || string.IsNullOrWhiteSpace(azureSettings.SubscriptionId))
        {
            return Ok(new ResourceHealthResponseDto
            {
                Status = "not_configured",
                Message = "Azure integration is not configured. Go to Settings > Integrations to set up Azure."
            });
        }

        var armEndpoint = _azureAuth.GetArmEndpoint(azureSettings.CloudEnvironment);
        string accessToken;
        try
        {
            accessToken = await _azureAuth.GetAccessTokenAsync(azureSettings, armEndpoint);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to acquire Azure token for Resource Health");
            return Ok(new ResourceHealthResponseDto
            {
                Status = "auth_error",
                Message = $"Authentication failed: {ex.Message}"
            });
        }

        var subscriptionId = azureSettings.SubscriptionId;
        var response = new ResourceHealthResponseDto
        {
            Status = "ok",
            SubscriptionId = subscriptionId
        };

        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        // Query Azure Resource Health availability statuses
        try
        {
            var url = $"{armEndpoint}/subscriptions/{subscriptionId}/providers/Microsoft.ResourceHealth/availabilityStatuses?api-version=2023-07-01-preview";
            var httpResponse = await httpClient.GetAsync(url);

            if (httpResponse.IsSuccessStatusCode)
            {
                var body = await httpResponse.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);

                if (doc.RootElement.TryGetProperty("value", out var healthArray))
                {
                    foreach (var item in healthArray.EnumerateArray())
                    {
                        var resourceId = item.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
                        // Resource Health ID format: /subscriptions/.../providers/Microsoft.ResourceHealth/availabilityStatuses/current
                        // Extract the actual resource ID by removing the trailing health provider path
                        var actualResourceId = ExtractResourceId(resourceId);

                        var props = item.TryGetProperty("properties", out var p) ? p : default;

                        var availabilityState = GetJsonString(props, "availabilityState") ?? "Unknown";
                        var summary = GetJsonString(props, "summary");
                        var reasonType = GetJsonString(props, "reasonType");
                        var occurredTime = GetJsonDateTime(props, "occurredTime");
                        var reportedTime = GetJsonDateTime(props, "reportedTime");

                        response.Resources.Add(new ResourceHealthStatusDto
                        {
                            ResourceId = actualResourceId,
                            ResourceName = ExtractResourceName(actualResourceId),
                            ResourceType = ExtractResourceType(actualResourceId),
                            ResourceGroup = ExtractResourceGroup(actualResourceId),
                            Location = GetJsonString(item, "location") ?? "",
                            AvailabilityState = NormalizeAvailabilityState(availabilityState),
                            Summary = summary,
                            ReasonType = reasonType,
                            OccurredTime = occurredTime,
                            ReportedTime = reportedTime
                        });
                    }
                }

                response.TotalResources = response.Resources.Count;
                response.AvailableCount = response.Resources.Count(r => r.AvailabilityState == "Available");
                response.DegradedCount = response.Resources.Count(r => r.AvailabilityState == "Degraded");
                response.UnavailableCount = response.Resources.Count(r => r.AvailabilityState == "Unavailable");
                response.UnknownCount = response.Resources.Count(r => r.AvailabilityState == "Unknown");
            }
            else
            {
                var errorBody = await httpResponse.Content.ReadAsStringAsync();
                _logger.LogWarning("Resource Health API returned {StatusCode}: {Body}",
                    httpResponse.StatusCode, errorBody);
                response.Status = "partial";
                response.Message = $"Resource Health API returned {httpResponse.StatusCode}. The Microsoft.ResourceHealth provider may need to be registered.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch resource health");
            response.Status = "error";
            response.Message = $"Failed to fetch resource health: {ex.Message}";
        }

        return Ok(response);
    }

    /// <summary>
    /// Get service health alerts for the configured subscription.
    /// Calls Azure Service Health events API.
    /// </summary>
    [HttpGet("alerts")]
    [ProducesResponseType(typeof(ServiceHealthResponseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ServiceHealthResponseDto>> GetServiceHealthAlerts()
    {
        var settings = IntegrationsController.LoadSettings();
        var azureSettings = settings.Azure;

        if (azureSettings == null || string.IsNullOrWhiteSpace(azureSettings.SubscriptionId))
        {
            return Ok(new ServiceHealthResponseDto
            {
                Status = "not_configured",
                Message = "Azure integration is not configured. Go to Settings > Integrations to set up Azure."
            });
        }

        var armEndpoint = _azureAuth.GetArmEndpoint(azureSettings.CloudEnvironment);
        string accessToken;
        try
        {
            accessToken = await _azureAuth.GetAccessTokenAsync(azureSettings, armEndpoint);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to acquire Azure token for Service Health");
            return Ok(new ServiceHealthResponseDto
            {
                Status = "auth_error",
                Message = $"Authentication failed: {ex.Message}"
            });
        }

        var subscriptionId = azureSettings.SubscriptionId;
        var response = new ServiceHealthResponseDto
        {
            Status = "ok",
            SubscriptionId = subscriptionId
        };

        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        try
        {
            // Service Health events (incidents, advisories, maintenance)
            var url = $"{armEndpoint}/subscriptions/{subscriptionId}/providers/Microsoft.ResourceHealth/events?api-version=2022-10-01&queryStartTime={DateTime.UtcNow.AddDays(-30):yyyy-MM-dd}";
            var httpResponse = await httpClient.GetAsync(url);

            if (httpResponse.IsSuccessStatusCode)
            {
                var body = await httpResponse.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);

                if (doc.RootElement.TryGetProperty("value", out var eventsArray))
                {
                    foreach (var evt in eventsArray.EnumerateArray())
                    {
                        var props = evt.TryGetProperty("properties", out var p) ? p : default;

                        response.Alerts.Add(new ServiceHealthAlertDto
                        {
                            Id = GetJsonString(evt, "id") ?? "",
                            Title = GetJsonString(props, "title") ?? "",
                            EventType = GetJsonString(props, "eventType") ?? "",
                            Status = GetJsonString(props, "status") ?? "",
                            Level = GetJsonString(props, "level") ?? "",
                            ImpactedService = ExtractImpactedService(props),
                            ImpactedRegion = ExtractImpactedRegion(props),
                            Description = GetJsonString(props, "summary"),
                            LastModifiedTime = GetJsonDateTime(props, "lastModifiedTime"),
                            ImpactStartTime = GetJsonDateTime(props, "impactStartTime")
                        });
                    }
                }

                response.TotalAlerts = response.Alerts.Count;
            }
            else
            {
                var errorBody = await httpResponse.Content.ReadAsStringAsync();
                _logger.LogWarning("Service Health API returned {StatusCode}: {Body}",
                    httpResponse.StatusCode, errorBody);
                response.Status = "partial";
                response.Message = $"Service Health API returned {httpResponse.StatusCode}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch service health alerts");
            response.Status = "error";
            response.Message = $"Failed to fetch service health alerts: {ex.Message}";
        }

        return Ok(response);
    }

    #region Helpers

    private static string ExtractResourceId(string healthStatusId)
    {
        // Format: /subscriptions/.../resourceGroups/.../providers/.../resourceName/providers/Microsoft.ResourceHealth/availabilityStatuses/current
        var marker = "/providers/Microsoft.ResourceHealth/";
        var idx = healthStatusId.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        return idx > 0 ? healthStatusId[..idx] : healthStatusId;
    }

    private static string ExtractResourceName(string resourceId)
    {
        if (string.IsNullOrEmpty(resourceId)) return "";
        var parts = resourceId.Split('/');
        return parts.Length > 0 ? parts[^1] : "";
    }

    private static string ExtractResourceType(string resourceId)
    {
        if (string.IsNullOrEmpty(resourceId)) return "";
        var parts = resourceId.Split('/');
        // Look for providers/Microsoft.*/typeName pattern
        for (int i = 0; i < parts.Length - 2; i++)
        {
            if (parts[i].Equals("providers", StringComparison.OrdinalIgnoreCase)
                && parts[i + 1].StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase))
            {
                // Return the last provider/type pair
                if (i + 2 < parts.Length - 1)
                    return $"{parts[i + 1]}/{parts[i + 2]}";
            }
        }
        return "";
    }

    private static string ExtractResourceGroup(string resourceId)
    {
        if (string.IsNullOrEmpty(resourceId)) return "";
        var parts = resourceId.Split('/');
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (parts[i].Equals("resourceGroups", StringComparison.OrdinalIgnoreCase))
                return parts[i + 1];
        }
        return "";
    }

    private static string NormalizeAvailabilityState(string state) => state?.ToLowerInvariant() switch
    {
        "available" => "Available",
        "degraded" => "Degraded",
        "unavailable" => "Unavailable",
        _ => "Unknown"
    };

    private static string? GetJsonString(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Undefined) return null;
        return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }

    private static DateTime? GetJsonDateTime(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Undefined) return null;
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            return DateTime.TryParse(prop.GetString(), out var dt) ? dt : null;
        }
        return null;
    }

    private static string? ExtractImpactedService(JsonElement props)
    {
        if (props.ValueKind == JsonValueKind.Undefined) return null;
        if (props.TryGetProperty("impact", out var impact) && impact.ValueKind == JsonValueKind.Array)
        {
            foreach (var svc in impact.EnumerateArray())
            {
                var name = GetJsonString(svc, "impactedService");
                if (!string.IsNullOrEmpty(name)) return name;
            }
        }
        return null;
    }

    private static string? ExtractImpactedRegion(JsonElement props)
    {
        if (props.ValueKind == JsonValueKind.Undefined) return null;
        if (props.TryGetProperty("impact", out var impact) && impact.ValueKind == JsonValueKind.Array)
        {
            foreach (var svc in impact.EnumerateArray())
            {
                if (svc.TryGetProperty("impactedRegions", out var regions) && regions.ValueKind == JsonValueKind.Array)
                {
                    foreach (var region in regions.EnumerateArray())
                    {
                        var name = GetJsonString(region, "impactedRegion");
                        if (!string.IsNullOrEmpty(name)) return name;
                    }
                }
            }
        }
        return null;
    }

    #endregion
}
