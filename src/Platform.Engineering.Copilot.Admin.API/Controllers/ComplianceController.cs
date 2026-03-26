using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Platform.Engineering.Copilot.Admin.API.DTOs;
using Platform.Engineering.Copilot.Admin.API.Services;

namespace Platform.Engineering.Copilot.Admin.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ComplianceController : ControllerBase
{
    private readonly ILogger<ComplianceController> _logger;
    private readonly IAzureAuthService _azureAuth;
    private static readonly JsonSerializerOptions _jsonOpts = new() { PropertyNameCaseInsensitive = true };

    // Cache scan results in memory (keyed by subscriptionId)
    private static ComplianceSummaryDto? _cachedSummary;
    private static DateTime _lastScanTime = DateTime.MinValue;
    private static readonly Dictionary<string, EnvironmentComplianceDetailDto> _cachedDetails = new();

    public ComplianceController(ILogger<ComplianceController> logger, IAzureAuthService azureAuth)
    {
        _logger = logger;
        _azureAuth = azureAuth;
    }

    [HttpGet("summary")]
    public async Task<ActionResult<ComplianceSummaryDto>> GetComplianceSummary()
    {
        var settings = IntegrationsController.LoadSettings();
        var azureSettings = settings.Azure;

        if (azureSettings == null || string.IsNullOrWhiteSpace(azureSettings.TenantId)
            || string.IsNullOrWhiteSpace(azureSettings.SubscriptionId))
        {
            return Ok(new ComplianceSummaryDto());
        }

        // Return cached if scanned in last 5 minutes
        if (_cachedSummary != null && (DateTime.UtcNow - _lastScanTime).TotalMinutes < 5)
        {
            return Ok(_cachedSummary);
        }

        try
        {
            var summary = await BuildComplianceSummary(azureSettings);
            _cachedSummary = summary;
            _lastScanTime = DateTime.UtcNow;
            return Ok(summary);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to build compliance summary from Azure");
            // Return cached data if available, otherwise empty
            return Ok(_cachedSummary ?? new ComplianceSummaryDto());
        }
    }

    [HttpPost("scan")]
    public async Task<IActionResult> RunComplianceScan([FromQuery] string? environmentId = null)
    {
        var settings = IntegrationsController.LoadSettings();
        var azureSettings = settings.Azure;

        if (azureSettings == null || string.IsNullOrWhiteSpace(azureSettings.SubscriptionId))
        {
            return BadRequest("Azure integration not configured");
        }

        try
        {
            if (!string.IsNullOrEmpty(environmentId))
            {
                _logger.LogInformation("Compliance scan initiated for environment {EnvironmentId}", environmentId);
                var detail = await BuildEnvironmentDetail(azureSettings, environmentId);
                _cachedDetails[environmentId] = detail;
            }
            else
            {
                _logger.LogInformation("Global compliance scan initiated");
                var summary = await BuildComplianceSummary(azureSettings);
                _cachedSummary = summary;
                _lastScanTime = DateTime.UtcNow;
            }
            return Accepted();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Compliance scan failed");
            return StatusCode(500, "Compliance scan failed: " + ex.Message);
        }
    }

    [HttpGet("environments/{environmentId}")]
    public async Task<ActionResult<EnvironmentComplianceDetailDto>> GetEnvironmentCompliance(string environmentId)
    {
        if (_cachedDetails.TryGetValue(environmentId, out var cached))
            return Ok(cached);

        var settings = IntegrationsController.LoadSettings();
        var azureSettings = settings.Azure;

        if (azureSettings == null || string.IsNullOrWhiteSpace(azureSettings.SubscriptionId))
        {
            return Ok(new EnvironmentComplianceDetailDto { EnvironmentId = environmentId });
        }

        try
        {
            var detail = await BuildEnvironmentDetail(azureSettings, environmentId);
            _cachedDetails[environmentId] = detail;
            return Ok(detail);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get environment compliance for {EnvironmentId}", environmentId);
            return Ok(new EnvironmentComplianceDetailDto { EnvironmentId = environmentId });
        }
    }

    [HttpPost("environments/{environmentId}/scan")]
    public async Task<IActionResult> ScanEnvironmentAsync(string environmentId)
    {
        var settings = IntegrationsController.LoadSettings();
        var azureSettings = settings.Azure;

        if (azureSettings == null || string.IsNullOrWhiteSpace(azureSettings.SubscriptionId))
        {
            return BadRequest("Azure integration not configured");
        }

        try
        {
            var detail = await BuildEnvironmentDetail(azureSettings, environmentId);
            _cachedDetails[environmentId] = detail;
            _logger.LogInformation("Environment compliance scan completed for {EnvironmentId}", environmentId);
            return Accepted();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Environment scan failed for {EnvironmentId}", environmentId);
            return StatusCode(500, "Scan failed: " + ex.Message);
        }
    }

    #region Azure REST API Integration

    private async Task<ComplianceSummaryDto> BuildComplianceSummary(AzureIntegrationSettingsDto azureSettings)
    {
        var armEndpoint = _azureAuth.GetArmEndpoint(azureSettings.CloudEnvironment);

        var accessToken = await _azureAuth.GetAccessTokenAsync(azureSettings, armEndpoint);
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var subscriptionId = azureSettings.SubscriptionId!;
        var summary = new ComplianceSummaryDto();

        // Fetch in parallel: policy compliance summary, resource groups, and security assessments
        var policyTask = FetchPolicyComplianceSummary(httpClient, armEndpoint, subscriptionId);
        var rgTask = FetchResourceGroups(httpClient, armEndpoint, subscriptionId);
        var securityTask = FetchSecurityAssessments(httpClient, armEndpoint, subscriptionId);

        await Task.WhenAll(policyTask, rgTask, securityTask);

        var (policyCompliant, policyNonCompliant, policyAssignments) = policyTask.Result;
        var resourceGroups = rgTask.Result;
        var (secCompliant, secNonCompliant, secViolations) = securityTask.Result;

        // Calculate overall controls and scores
        var totalControls = policyCompliant + policyNonCompliant + secCompliant + secNonCompliant;
        var compliantControls = policyCompliant + secCompliant;
        var nonCompliantControls = policyNonCompliant + secNonCompliant;
        var overallScore = totalControls > 0 ? (decimal)compliantControls / totalControls * 100m : 100m;

        summary.TotalControls = totalControls;
        summary.CompliantControls = compliantControls;
        summary.NonCompliantControls = nonCompliantControls;
        summary.OverallScore = Math.Round(overallScore, 1);

        // Framework scores
        var policyTotal = policyCompliant + policyNonCompliant;
        var policyScore = policyTotal > 0 ? (decimal)policyCompliant / policyTotal * 100m : 100m;
        summary.FrameworkScores.Add(new FrameworkScoreDto
        {
            Framework = "Azure Policy",
            Score = Math.Round(policyScore, 1),
            CompliantControls = policyCompliant,
            TotalControls = policyTotal
        });

        var secTotal = secCompliant + secNonCompliant;
        var secScore = secTotal > 0 ? (decimal)secCompliant / secTotal * 100m : 100m;
        summary.FrameworkScores.Add(new FrameworkScoreDto
        {
            Framework = "Security Center",
            Score = Math.Round(secScore, 1),
            CompliantControls = secCompliant,
            TotalControls = secTotal
        });

        // Build environment statuses from resource groups
        foreach (var rg in resourceGroups)
        {
            var rgPolicyResult = await FetchResourceGroupPolicyState(httpClient, armEndpoint, subscriptionId, rg.Name);
            var rgTotal = rgPolicyResult.compliant + rgPolicyResult.nonCompliant;
            var rgScore = rgTotal > 0 ? (decimal)rgPolicyResult.compliant / rgTotal * 100m : 100m;

            summary.EnvironmentStatuses.Add(new EnvironmentComplianceStatusDto
            {
                EnvironmentId = rg.Name,
                EnvironmentName = rg.Name,
                Status = rgPolicyResult.nonCompliant == 0 ? "Compliant" : "NonCompliant",
                ComplianceScore = Math.Round(rgScore, 1),
                CriticalViolations = rgPolicyResult.critical,
                HighViolations = rgPolicyResult.high,
                LastScannedAt = DateTime.UtcNow
            });
        }

        // Top violations from security assessments
        summary.TopViolations = secViolations.Take(10).ToList();

        // Also add non-compliant policy assignments as violations
        foreach (var pa in policyAssignments.Take(5))
        {
            summary.TopViolations.Add(pa);
        }

        return summary;
    }

    private async Task<EnvironmentComplianceDetailDto> BuildEnvironmentDetail(
        AzureIntegrationSettingsDto azureSettings, string environmentId)
    {
        var armEndpoint = _azureAuth.GetArmEndpoint(azureSettings.CloudEnvironment);

        var accessToken = await _azureAuth.GetAccessTokenAsync(azureSettings, armEndpoint);
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var subscriptionId = azureSettings.SubscriptionId!;

        // environmentId is the resource group name
        var rgPolicy = await FetchResourceGroupPolicyState(httpClient, armEndpoint, subscriptionId, environmentId);
        var rgResources = await FetchResourceGroupResources(httpClient, armEndpoint, subscriptionId, environmentId);
        var rgTotal = rgPolicy.compliant + rgPolicy.nonCompliant;
        var rgScore = rgTotal > 0 ? (decimal)rgPolicy.compliant / rgTotal * 100m : 100m;

        var detail = new EnvironmentComplianceDetailDto
        {
            EnvironmentId = environmentId,
            EnvironmentName = environmentId,
            SubscriptionId = subscriptionId,
            SubscriptionName = azureSettings.SubscriptionId ?? "",
            OverallScore = Math.Round(rgScore, 1),
            ComplianceScore = Math.Round(rgScore, 1),
            Status = rgPolicy.nonCompliant == 0 ? "Compliant" : "NonCompliant",
            LastScannedAt = DateTime.UtcNow,
            FrameworkScores = new List<FrameworkScoreDto>
            {
                new()
                {
                    Framework = "Azure Policy",
                    Score = Math.Round(rgScore, 1),
                    CompliantControls = rgPolicy.compliant,
                    TotalControls = rgTotal
                }
            },
            Controls = rgPolicy.controls,
            Resources = rgResources
        };

        return detail;
    }

    /// <summary>
    /// Fetch Azure Policy compliance summary for the subscription.
    /// Uses POST .../policyStates/latest/summarize
    /// </summary>
    private async Task<(int compliant, int nonCompliant, List<ControlViolationDto> violations)> FetchPolicyComplianceSummary(
        HttpClient httpClient, string armEndpoint, string subscriptionId)
    {
        int compliant = 0, nonCompliant = 0;
        var violations = new List<ControlViolationDto>();

        try
        {
            var url = $"{armEndpoint}/subscriptions/{subscriptionId}/providers/Microsoft.PolicyInsights/policyStates/latest/summarize?api-version=2019-10-01";
            var response = await httpClient.PostAsync(url, null);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Policy compliance summarize returned {Status}", response.StatusCode);
                return (0, 0, violations);
            }

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);

            if (doc.RootElement.TryGetProperty("value", out var results) && results.GetArrayLength() > 0)
            {
                var summary = results[0];
                if (summary.TryGetProperty("results", out var topResults))
                {
                    nonCompliant = topResults.TryGetProperty("nonCompliantResources", out var ncRes) ? ncRes.GetInt32() : 0;
                    compliant = topResults.TryGetProperty("resourceDetails", out var details)
                        ? details.EnumerateArray()
                            .Where(d => d.TryGetProperty("complianceState", out var cs) && cs.GetString() == "compliant")
                            .Sum(d => d.TryGetProperty("count", out var c) ? c.GetInt32() : 0)
                        : 0;

                    // If we can't parse compliant count from details, estimate from total
                    if (compliant == 0 && topResults.TryGetProperty("totalResources", out var totalRes))
                    {
                        compliant = totalRes.GetInt32() - nonCompliant;
                    }
                }

                // Extract policy assignment violations
                if (summary.TryGetProperty("policyAssignments", out var assignments))
                {
                    foreach (var pa in assignments.EnumerateArray().Take(10))
                    {
                        var paResults = pa.TryGetProperty("results", out var par) ? par : default;
                        var ncCount = paResults.ValueKind != JsonValueKind.Undefined
                            && paResults.TryGetProperty("nonCompliantResources", out var nc)
                            ? nc.GetInt32() : 0;

                        if (ncCount > 0)
                        {
                            var paId = pa.TryGetProperty("policyAssignmentId", out var pid)
                                ? pid.GetString() ?? "" : "";
                            var paName = paId.Contains("/") ? paId.Split('/').Last() : paId;

                            violations.Add(new ControlViolationDto
                            {
                                ControlId = "Policy",
                                ControlName = paName,
                                Severity = ncCount >= 10 ? "Critical" : ncCount >= 5 ? "High" : "Medium",
                                Description = $"Policy assignment has {ncCount} non-compliant resources",
                                AffectedResourceCount = ncCount
                            });
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch policy compliance summary");
        }

        return (compliant, nonCompliant, violations);
    }

    /// <summary>
    /// Fetch Security Center assessments for the subscription.
    /// </summary>
    private async Task<(int compliant, int nonCompliant, List<ControlViolationDto> violations)> FetchSecurityAssessments(
        HttpClient httpClient, string armEndpoint, string subscriptionId)
    {
        int compliant = 0, nonCompliant = 0;
        var violations = new List<ControlViolationDto>();

        try
        {
            var url = $"{armEndpoint}/subscriptions/{subscriptionId}/providers/Microsoft.Security/assessments?api-version=2021-06-01";
            var response = await httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Security assessments returned {Status}", response.StatusCode);
                return (0, 0, violations);
            }

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);

            if (doc.RootElement.TryGetProperty("value", out var assessments))
            {
                foreach (var assessment in assessments.EnumerateArray())
                {
                    var props = assessment.TryGetProperty("properties", out var p) ? p : default;
                    if (props.ValueKind == JsonValueKind.Undefined) continue;

                    var status = props.TryGetProperty("status", out var s) ? s : default;
                    var statusCode = status.ValueKind != JsonValueKind.Undefined
                        && status.TryGetProperty("code", out var sc) ? sc.GetString() : null;

                    if (statusCode == "Healthy")
                    {
                        compliant++;
                    }
                    else if (statusCode == "Unhealthy" || statusCode == "NotApplicable" == false)
                    {
                        nonCompliant++;

                        var displayName = props.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? "" : "";
                        var severity = props.TryGetProperty("metadata", out var meta)
                            && meta.TryGetProperty("severity", out var sev)
                            ? sev.GetString() ?? "Medium" : "Medium";

                        var resId = assessment.TryGetProperty("id", out var aid) ? aid.GetString() ?? "" : "";

                        violations.Add(new ControlViolationDto
                        {
                            ControlId = "SEC",
                            ControlName = displayName.Length > 60 ? displayName[..60] + "..." : displayName,
                            Severity = severity,
                            Description = displayName,
                            AffectedResourceCount = 1
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch security assessments");
        }

        return (compliant, nonCompliant, violations);
    }

    /// <summary>
    /// Fetch resource groups from Azure.
    /// </summary>
    private async Task<List<(string Name, string Location)>> FetchResourceGroups(
        HttpClient httpClient, string armEndpoint, string subscriptionId)
    {
        var rgs = new List<(string Name, string Location)>();
        try
        {
            var url = $"{armEndpoint}/subscriptions/{subscriptionId}/resourcegroups?api-version=2021-04-01";
            var response = await httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) return rgs;

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);

            if (doc.RootElement.TryGetProperty("value", out var rgArray))
            {
                foreach (var rg in rgArray.EnumerateArray())
                {
                    var name = rg.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    var location = rg.TryGetProperty("location", out var l) ? l.GetString() ?? "" : "";
                    rgs.Add((name, location));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch resource groups for compliance");
        }
        return rgs;
    }

    /// <summary>
    /// Fetch policy compliance state for a specific resource group.
    /// </summary>
    private async Task<(int compliant, int nonCompliant, int critical, int high, List<ControlComplianceDetailDto> controls)>
        FetchResourceGroupPolicyState(HttpClient httpClient, string armEndpoint, string subscriptionId, string resourceGroupName)
    {
        int compliant = 0, nonCompliant = 0, critical = 0, high = 0;
        var controls = new List<ControlComplianceDetailDto>();

        try
        {
            var url = $"{armEndpoint}/subscriptions/{subscriptionId}/resourceGroups/{Uri.EscapeDataString(resourceGroupName)}/providers/Microsoft.PolicyInsights/policyStates/latest/summarize?api-version=2019-10-01";
            var response = await httpClient.PostAsync(url, null);
            if (!response.IsSuccessStatusCode) return (0, 0, 0, 0, controls);

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);

            if (doc.RootElement.TryGetProperty("value", out var results) && results.GetArrayLength() > 0)
            {
                var summary = results[0];
                if (summary.TryGetProperty("results", out var topResults))
                {
                    nonCompliant = topResults.TryGetProperty("nonCompliantResources", out var ncRes) ? ncRes.GetInt32() : 0;
                    if (topResults.TryGetProperty("totalResources", out var totalRes))
                    {
                        compliant = totalRes.GetInt32() - nonCompliant;
                    }
                }

                // Parse policy assignment details for this RG
                if (summary.TryGetProperty("policyAssignments", out var assignments))
                {
                    foreach (var pa in assignments.EnumerateArray())
                    {
                        var paResults = pa.TryGetProperty("results", out var par) ? par : default;
                        var ncCount = paResults.ValueKind != JsonValueKind.Undefined
                            && paResults.TryGetProperty("nonCompliantResources", out var nc)
                            ? nc.GetInt32() : 0;

                        var paId = pa.TryGetProperty("policyAssignmentId", out var pid) ? pid.GetString() ?? "" : "";
                        var paName = paId.Contains("/") ? paId.Split('/').Last() : paId;
                        var isCompliant = ncCount == 0;

                        var severity = ncCount >= 10 ? "Critical" : ncCount >= 5 ? "High" : ncCount > 0 ? "Medium" : "Low";
                        if (severity == "Critical") critical += ncCount;
                        else if (severity == "High") high += ncCount;

                        controls.Add(new ControlComplianceDetailDto
                        {
                            ControlId = "Policy",
                            ControlName = paName,
                            Framework = "Azure Policy",
                            Status = isCompliant ? "Compliant" : "NonCompliant",
                            Severity = severity,
                            Description = $"{ncCount} non-compliant resources",
                            AffectedResources = new List<string>(),
                            RemediationGuidance = isCompliant ? null : "Review policy assignment and remediate non-compliant resources"
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch policy state for resource group {RG}", resourceGroupName);
        }

        return (compliant, nonCompliant, critical, high, controls);
    }

    /// <summary>
    /// Fetch resources in a resource group for compliance detail view.
    /// </summary>
    private async Task<List<ResourceComplianceDto>> FetchResourceGroupResources(
        HttpClient httpClient, string armEndpoint, string subscriptionId, string resourceGroupName)
    {
        var resources = new List<ResourceComplianceDto>();
        try
        {
            var url = $"{armEndpoint}/subscriptions/{subscriptionId}/resourceGroups/{Uri.EscapeDataString(resourceGroupName)}/resources?api-version=2021-04-01";
            var response = await httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) return resources;

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);

            if (doc.RootElement.TryGetProperty("value", out var resArray))
            {
                foreach (var res in resArray.EnumerateArray())
                {
                    var resId = res.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "";
                    var resName = res.TryGetProperty("name", out var rn) ? rn.GetString() ?? "" : "";
                    var resType = res.TryGetProperty("type", out var rt) ? rt.GetString() ?? "" : "";

                    resources.Add(new ResourceComplianceDto
                    {
                        ResourceId = resId,
                        ResourceName = resName,
                        ResourceType = resType,
                        IsCompliant = true, // Default; per-resource policy state requires additional calls
                        ViolationCount = 0,
                        FailedControls = new List<string>()
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch resources for resource group {RG}", resourceGroupName);
        }
        return resources;
    }

    #endregion
}
