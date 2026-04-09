using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.Engineering.Copilot.Agents.Common;
using Platform.Engineering.Copilot.Agents.DevOps.Configuration;
using Platform.Engineering.Copilot.Core.Configuration;

namespace Platform.Engineering.Copilot.Agents.DevOps.Tools.AzureDevOps;

/// <summary>
/// Tool for querying Azure DevOps work items (User Stories, Tasks, Bugs, Epics, Features, etc.).
/// Supports filtering by type, state, assigned user, and custom WIQL.
/// </summary>
public class ListADOWorkItemsTool : BaseTool
{
    public override string Name => "list_ado_work_items";

    public override string Description =>
        "List work items (User Stories, Tasks, Bugs, Epics, Features) from an Azure DevOps project. " +
        "Supports filtering by type, state, assigned user, iteration, or area path. " +
        "Use this to review backlog, sprint work, bug counts, or any work tracking query.";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GatewayOptions _gatewayOptions;
    private readonly DevOpsAgentOptions _devOpsOptions;

    public ListADOWorkItemsTool(
        ILogger<ListADOWorkItemsTool> logger,
        IHttpClientFactory httpClientFactory,
        IOptions<GatewayOptions> gatewayOptions,
        IOptions<DevOpsAgentOptions> devOpsOptions)
        : base(logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _gatewayOptions = gatewayOptions?.Value ?? throw new ArgumentNullException(nameof(gatewayOptions));
        _devOpsOptions = devOpsOptions?.Value ?? throw new ArgumentNullException(nameof(devOpsOptions));

        Parameters.Add(new ToolParameter("project", "Project name to query work items from", true));
        Parameters.Add(new ToolParameter("type", "Work item type filter: Bug, Task, User Story, Epic, Feature, Issue (optional — returns all types if omitted)", false));
        Parameters.Add(new ToolParameter("state", "State filter: Active, New, Resolved, Closed, Done (optional)", false));
        Parameters.Add(new ToolParameter("assigned_to", "Filter by assigned user display name or 'me' (optional)", false));
        Parameters.Add(new ToolParameter("limit", "Maximum number of work items to return (default: 50, max: 200)", false));
        Parameters.Add(new ToolParameter("wiql", "Custom WIQL query string to override all filters (optional, advanced)", false));
    }

    public override async Task<string> ExecuteAsync(
        IDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var adoGw = _gatewayOptions.AzureDevOps;

            if (!adoGw.Enabled || string.IsNullOrWhiteSpace(adoGw.AccessToken) || string.IsNullOrWhiteSpace(adoGw.ServerUrl))
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = "Azure DevOps is not configured. Please set the server URL and PAT in Settings → Integrations → Azure DevOps."
                });
            }

            var project = arguments.TryGetValue("project", out var projVal) ? projVal?.ToString() : null;
            project ??= _devOpsOptions.AzureDevOps.DefaultProject;
            if (string.IsNullOrWhiteSpace(project))
            {
                return JsonSerializer.Serialize(new { success = false, error = "Project name is required." });
            }

            var type = arguments.TryGetValue("type", out var typeVal) ? typeVal?.ToString() : null;
            var state = arguments.TryGetValue("state", out var stateVal) ? stateVal?.ToString() : null;
            var assignedTo = arguments.TryGetValue("assigned_to", out var assignedVal) ? assignedVal?.ToString() : null;
            var limit = 50;
            if (arguments.TryGetValue("limit", out var limitVal))
            {
                if (limitVal is int li) limit = Math.Min(li, 200);
                else if (int.TryParse(limitVal?.ToString(), out var lp)) limit = Math.Min(lp, 200);
            }
            var customWiql = arguments.TryGetValue("wiql", out var wiqlVal) ? wiqlVal?.ToString() : null;

            var baseUrl = BuildBaseUrl(adoGw);
            using var client = CreateAuthenticatedClient(adoGw.AccessToken);

            // Step 1: Run WIQL query to get IDs
            var wiql = customWiql ?? BuildWiql(project, type, state, assignedTo, limit);
            var wiqlUrl = $"{baseUrl}/{Uri.EscapeDataString(project)}/_apis/wit/wiql?api-version=6.0&$top={limit}";

            Logger.LogInformation("Running ADO WIQL query for project {Project}", project);

            var wiqlBody = JsonSerializer.Serialize(new { query = wiql });
            var wiqlContent = new StringContent(wiqlBody, Encoding.UTF8, "application/json");
            var wiqlResp = await client.PostAsync(wiqlUrl, wiqlContent, cancellationToken);

            if (!wiqlResp.IsSuccessStatusCode)
            {
                var err = await wiqlResp.Content.ReadAsStringAsync(cancellationToken);
                return JsonSerializer.Serialize(new { success = false, error = $"WIQL query failed ({(int)wiqlResp.StatusCode}): {Truncate(err, 400)}" });
            }

            var wiqlResult = await wiqlResp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            if (!wiqlResult.TryGetProperty("workItems", out var workItemRefs) || workItemRefs.GetArrayLength() == 0)
            {
                return JsonSerializer.Serialize(new { success = true, project, count = 0, workItems = Array.Empty<object>(), message = "No work items found matching the filters." });
            }

            // Step 2: Fetch work item details in batch
            var ids = workItemRefs.EnumerateArray()
                .Select(w => w.TryGetProperty("id", out var id) ? id.GetInt32() : 0)
                .Where(id => id > 0)
                .Take(limit)
                .ToList();

            var fields = "System.Id,System.Title,System.WorkItemType,System.State,System.AssignedTo,System.CreatedDate,System.ChangedDate,System.Description,Microsoft.VSTS.Common.Priority,System.AreaPath,System.IterationPath,System.Tags";
            var detailsUrl = $"{baseUrl}/_apis/wit/workitems?ids={string.Join(",", ids)}&fields={fields}&api-version=6.0";

            var detailsResp = await client.GetAsync(detailsUrl, cancellationToken);
            if (!detailsResp.IsSuccessStatusCode)
            {
                var err = await detailsResp.Content.ReadAsStringAsync(cancellationToken);
                return JsonSerializer.Serialize(new { success = false, error = $"Fetching work item details failed ({(int)detailsResp.StatusCode}): {Truncate(err, 400)}" });
            }

            var detailsJson = await detailsResp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            var workItems = new List<object>();

            if (detailsJson.TryGetProperty("value", out var items))
            {
                foreach (var item in items.EnumerateArray())
                {
                    var f = item.TryGetProperty("fields", out var fields2) ? fields2 : default;
                    workItems.Add(new
                    {
                        id = item.TryGetProperty("id", out var idProp) ? idProp.GetInt32() : 0,
                        url = item.TryGetProperty("url", out var urlProp) ? urlProp.GetString() : null,
                        type = GetField(f, "System.WorkItemType"),
                        title = GetField(f, "System.Title"),
                        state = GetField(f, "System.State"),
                        assignedTo = GetAssignedTo(f),
                        priority = GetField(f, "Microsoft.VSTS.Common.Priority"),
                        areaPath = GetField(f, "System.AreaPath"),
                        iterationPath = GetField(f, "System.IterationPath"),
                        tags = GetField(f, "System.Tags"),
                        createdDate = GetField(f, "System.CreatedDate"),
                        changedDate = GetField(f, "System.ChangedDate"),
                    });
                }
            }

            return JsonSerializer.Serialize(new
            {
                success = true,
                project,
                count = workItems.Count,
                workItems
            }, new JsonSerializerOptions { WriteIndented = false });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error listing ADO work items");
            return JsonSerializer.Serialize(new { success = false, error = ex.Message });
        }
    }

    private string BuildWiql(string project, string? type, string? state, string? assignedTo, int limit)
    {
        var conditions = new List<string>
        {
            $"[System.TeamProject] = '{project.Replace("'", "''")}'"
        };

        if (!string.IsNullOrWhiteSpace(type))
            conditions.Add($"[System.WorkItemType] = '{type.Replace("'", "''")}'");

        if (!string.IsNullOrWhiteSpace(state))
            conditions.Add($"[System.State] = '{state.Replace("'", "''")}'");

        if (!string.IsNullOrWhiteSpace(assignedTo))
        {
            conditions.Add(assignedTo.Equals("me", StringComparison.OrdinalIgnoreCase)
                ? "[System.AssignedTo] = @Me"
                : $"[System.AssignedTo] contains '{assignedTo.Replace("'", "''")}'");
        }

        return $"SELECT [System.Id] FROM WorkItems WHERE {string.Join(" AND ", conditions)} ORDER BY [System.ChangedDate] DESC";
    }

    private string BuildBaseUrl(AzureDevOpsGatewayOptions adoGw)
    {
        var url = adoGw.ServerUrl!.TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(adoGw.DefaultCollection))
            url = $"{url}/{adoGw.DefaultCollection.Trim('/')}";
        return url;
    }

    private HttpClient CreateAuthenticatedClient(string accessToken)
    {
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        var encoded = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{accessToken}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", encoded);
        return client;
    }

    private static string? GetField(JsonElement fields, string key)
    {
        if (fields.ValueKind == JsonValueKind.Undefined) return null;
        return fields.TryGetProperty(key, out var val) ? val.ToString() : null;
    }

    private static string? GetAssignedTo(JsonElement fields)
    {
        if (fields.ValueKind == JsonValueKind.Undefined) return null;
        if (!fields.TryGetProperty("System.AssignedTo", out var assigned)) return null;
        if (assigned.ValueKind == JsonValueKind.Object)
            return assigned.TryGetProperty("displayName", out var dn) ? dn.GetString() : assigned.ToString();
        return assigned.ToString();
    }

    private static string Truncate(string s, int max) => s.Length > max ? s[..max] + "..." : s;
}
