using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Platform.Engineering.Copilot.Admin.API.DTOs;
using Platform.Engineering.Copilot.Admin.API.Services;

namespace Platform.Engineering.Copilot.Admin.API.Controllers;

/// <summary>
/// API controller for Azure Cost Management.
/// Queries real Azure Cost Management APIs for spend data.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class CostController : ControllerBase
{
    private readonly ILogger<CostController> _logger;
    private readonly IAzureAuthService _azureAuth;
    private static readonly JsonSerializerOptions _jsonOpts = new() { PropertyNameCaseInsensitive = true };

    // Cache cost data (30-minute TTL — cost data doesn't change minute-to-minute)
    private static CostSummaryDto? _cachedSummary;
    private static CostByResourceGroupDto? _cachedByRg;
    private static DateTime _lastSummaryTime = DateTime.MinValue;
    private static DateTime _lastByRgTime = DateTime.MinValue;

    public CostController(ILogger<CostController> logger, IAzureAuthService azureAuth)
    {
        _logger = logger;
        _azureAuth = azureAuth;
    }

    /// <summary>
    /// Get cost summary for the subscription — last 30 days daily costs + cost by service.
    /// </summary>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(CostSummaryDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<CostSummaryDto>> GetCostSummary([FromQuery] bool forceRefresh = false)
    {
        var settings = IntegrationsController.LoadSettings();
        var azureSettings = settings.Azure;

        if (azureSettings == null || string.IsNullOrWhiteSpace(azureSettings.SubscriptionId))
        {
            return Ok(new CostSummaryDto
            {
                Status = "not_configured",
                Message = "Azure integration is not configured. Go to Settings > Integrations to set up Azure."
            });
        }

        if (!forceRefresh && _cachedSummary != null && (DateTime.UtcNow - _lastSummaryTime).TotalMinutes < 30)
        {
            return Ok(_cachedSummary);
        }

        var armEndpoint = _azureAuth.GetArmEndpoint(azureSettings.CloudEnvironment);
        string accessToken;
        try
        {
            accessToken = await _azureAuth.GetAccessTokenAsync(azureSettings, armEndpoint);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to acquire Azure token for Cost Management");
            return Ok(new CostSummaryDto
            {
                Status = "auth_error",
                Message = $"Authentication failed: {ex.Message}"
            });
        }

        var subscriptionId = azureSettings.SubscriptionId!;
        var response = new CostSummaryDto
        {
            Status = "ok",
            SubscriptionId = subscriptionId
        };

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        // Fetch daily costs and cost by service in parallel
        var dailyTask = FetchDailyCosts(httpClient, armEndpoint, subscriptionId);
        var serviceTask = FetchCostByService(httpClient, armEndpoint, subscriptionId);

        await Task.WhenAll(dailyTask, serviceTask);

        response.DailyCosts = dailyTask.Result;
        response.CostByService = serviceTask.Result;
        response.TotalCostLast30Days = response.DailyCosts.Sum(d => d.Cost);
        if (response.DailyCosts.Count > 0)
            response.Currency = response.DailyCosts[0].Currency;

        _cachedSummary = response;
        _lastSummaryTime = DateTime.UtcNow;

        _logger.LogInformation("Cost summary: ${Total:N2} over last 30 days", response.TotalCostLast30Days);

        return Ok(response);
    }

    /// <summary>
    /// Get cost breakdown by resource group for the last 30 days.
    /// </summary>
    [HttpGet("by-resource-group")]
    [ProducesResponseType(typeof(CostByResourceGroupDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<CostByResourceGroupDto>> GetCostByResourceGroup([FromQuery] bool forceRefresh = false)
    {
        var settings = IntegrationsController.LoadSettings();
        var azureSettings = settings.Azure;

        if (azureSettings == null || string.IsNullOrWhiteSpace(azureSettings.SubscriptionId))
        {
            return Ok(new CostByResourceGroupDto
            {
                Status = "not_configured",
                Message = "Azure integration is not configured."
            });
        }

        if (!forceRefresh && _cachedByRg != null && (DateTime.UtcNow - _lastByRgTime).TotalMinutes < 30)
        {
            return Ok(_cachedByRg);
        }

        var armEndpoint = _azureAuth.GetArmEndpoint(azureSettings.CloudEnvironment);
        string accessToken;
        try
        {
            accessToken = await _azureAuth.GetAccessTokenAsync(azureSettings, armEndpoint);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to acquire Azure token for Cost Management");
            return Ok(new CostByResourceGroupDto
            {
                Status = "auth_error",
                Message = $"Authentication failed: {ex.Message}"
            });
        }

        var subscriptionId = azureSettings.SubscriptionId!;

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = new CostByResourceGroupDto
        {
            Status = "ok",
            SubscriptionId = subscriptionId,
            ResourceGroups = await FetchCostByResourceGroup(httpClient, armEndpoint, subscriptionId)
        };

        _cachedByRg = response;
        _lastByRgTime = DateTime.UtcNow;

        return Ok(response);
    }

    #region Azure Cost Management API Calls

    private async Task<List<DailyCostDto>> FetchDailyCosts(
        HttpClient httpClient, string armEndpoint, string subscriptionId)
    {
        var results = new List<DailyCostDto>();
        var from = DateTime.UtcNow.AddDays(-30).ToString("yyyy-MM-dd");
        var to = DateTime.UtcNow.ToString("yyyy-MM-dd");

        try
        {
            var url = $"{armEndpoint}/subscriptions/{subscriptionId}/providers/Microsoft.CostManagement/query?api-version=2023-11-01";
            var requestBody = new
            {
                type = "ActualCost",
                timeframe = "Custom",
                timePeriod = new { from, to },
                dataset = new
                {
                    granularity = "Daily",
                    aggregation = new
                    {
                        totalCost = new { name = "Cost", function = "Sum" }
                    }
                }
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var httpResponse = await httpClient.PostAsync(url, content);

            if (!httpResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Cost Management daily query returned {StatusCode}", httpResponse.StatusCode);
                return results;
            }

            var json = await httpResponse.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("properties", out var props) &&
                props.TryGetProperty("rows", out var rows))
            {
                foreach (var row in rows.EnumerateArray())
                {
                    if (row.GetArrayLength() >= 3)
                    {
                        var cost = row[0].GetDecimal();
                        var dateInt = row[1].GetInt32(); // YYYYMMDD format
                        var currency = row[2].GetString() ?? "USD";

                        var dateStr = dateInt.ToString();
                        if (DateTime.TryParseExact(dateStr, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var date))
                        {
                            results.Add(new DailyCostDto
                            {
                                Date = date,
                                Cost = Math.Round(cost, 2),
                                Currency = currency
                            });
                        }
                    }
                }
            }

            _logger.LogInformation("Retrieved {Count} daily cost records", results.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch daily costs");
        }

        return results;
    }

    private async Task<List<ServiceCostDto>> FetchCostByService(
        HttpClient httpClient, string armEndpoint, string subscriptionId)
    {
        var results = new List<ServiceCostDto>();
        var from = DateTime.UtcNow.AddDays(-30).ToString("yyyy-MM-dd");
        var to = DateTime.UtcNow.ToString("yyyy-MM-dd");

        try
        {
            var url = $"{armEndpoint}/subscriptions/{subscriptionId}/providers/Microsoft.CostManagement/query?api-version=2023-11-01";
            var requestBody = new
            {
                type = "ActualCost",
                timeframe = "Custom",
                timePeriod = new { from, to },
                dataset = new
                {
                    granularity = "None",
                    aggregation = new
                    {
                        totalCost = new { name = "Cost", function = "Sum" }
                    },
                    grouping = new[]
                    {
                        new { type = "Dimension", name = "ServiceName" }
                    }
                }
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var httpResponse = await httpClient.PostAsync(url, content);

            if (!httpResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Cost Management service query returned {StatusCode}", httpResponse.StatusCode);
                return results;
            }

            var json = await httpResponse.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("properties", out var props) &&
                props.TryGetProperty("rows", out var rows))
            {
                foreach (var row in rows.EnumerateArray())
                {
                    if (row.GetArrayLength() >= 3)
                    {
                        var cost = row[0].GetDecimal();
                        var serviceName = row[2].GetString() ?? "Unknown";
                        var currency = row[1].GetString() ?? "USD";

                        results.Add(new ServiceCostDto
                        {
                            ServiceName = serviceName,
                            Cost = Math.Round(cost, 2),
                            Currency = currency
                        });
                    }
                }
            }

            // Sort by cost descending, take top 20
            results = results.OrderByDescending(r => r.Cost).Take(20).ToList();
            _logger.LogInformation("Retrieved {Count} service cost records", results.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch cost by service");
        }

        return results;
    }

    private async Task<List<ResourceGroupCostDto>> FetchCostByResourceGroup(
        HttpClient httpClient, string armEndpoint, string subscriptionId)
    {
        var results = new List<ResourceGroupCostDto>();
        var from = DateTime.UtcNow.AddDays(-30).ToString("yyyy-MM-dd");
        var to = DateTime.UtcNow.ToString("yyyy-MM-dd");

        try
        {
            var url = $"{armEndpoint}/subscriptions/{subscriptionId}/providers/Microsoft.CostManagement/query?api-version=2023-11-01";
            var requestBody = new
            {
                type = "ActualCost",
                timeframe = "Custom",
                timePeriod = new { from, to },
                dataset = new
                {
                    granularity = "None",
                    aggregation = new
                    {
                        totalCost = new { name = "Cost", function = "Sum" }
                    },
                    grouping = new[]
                    {
                        new { type = "Dimension", name = "ResourceGroupName" }
                    }
                }
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var httpResponse = await httpClient.PostAsync(url, content);

            if (!httpResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Cost Management RG query returned {StatusCode}", httpResponse.StatusCode);
                return results;
            }

            var json = await httpResponse.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("properties", out var props) &&
                props.TryGetProperty("rows", out var rows))
            {
                foreach (var row in rows.EnumerateArray())
                {
                    if (row.GetArrayLength() >= 3)
                    {
                        var cost = row[0].GetDecimal();
                        var rgName = row[2].GetString() ?? "Unknown";
                        var currency = row[1].GetString() ?? "USD";

                        results.Add(new ResourceGroupCostDto
                        {
                            ResourceGroupName = rgName,
                            Cost = Math.Round(cost, 2),
                            Currency = currency
                        });
                    }
                }
            }

            results = results.OrderByDescending(r => r.Cost).Take(30).ToList();
            _logger.LogInformation("Retrieved {Count} resource group cost records", results.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch cost by resource group");
        }

        return results;
    }

    #endregion
}
