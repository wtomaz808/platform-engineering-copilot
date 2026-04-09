using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.Engineering.Copilot.Agents.Common;
using Platform.Engineering.Copilot.Agents.DevOps.Configuration;
using Platform.Engineering.Copilot.Core.Configuration;

namespace Platform.Engineering.Copilot.Agents.DevOps.Tools.AzureDevOps;

/// <summary>
/// Tool for creating Azure DevOps work items (User Stories, Tasks, Bugs, Epics, Features).
/// </summary>
public class CreateADOWorkItemTool : BaseTool
{
    public override string Name => "create_ado_work_item";

    public override string Description =>
        "Create a new work item in an Azure DevOps project. " +
        "Supports User Story, Task, Bug, Epic, Feature, and Issue types. " +
        "Use this to log bugs, create tasks, add user stories to the backlog, or create epics.";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GatewayOptions _gatewayOptions;
    private readonly DevOpsAgentOptions _devOpsOptions;

    public CreateADOWorkItemTool(
        ILogger<CreateADOWorkItemTool> logger,
        IHttpClientFactory httpClientFactory,
        IOptions<GatewayOptions> gatewayOptions,
        IOptions<DevOpsAgentOptions> devOpsOptions)
        : base(logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _gatewayOptions = gatewayOptions?.Value ?? throw new ArgumentNullException(nameof(gatewayOptions));
        _devOpsOptions = devOpsOptions?.Value ?? throw new ArgumentNullException(nameof(devOpsOptions));

        Parameters.Add(new ToolParameter("project", "Project name to create the work item in", true));
        Parameters.Add(new ToolParameter("type", "Work item type: Bug, Task, User Story, Epic, Feature, Issue", true));
        Parameters.Add(new ToolParameter("title", "Title of the work item", true));
        Parameters.Add(new ToolParameter("description", "Description / acceptance criteria (optional)", false));
        Parameters.Add(new ToolParameter("assigned_to", "Display name of user to assign to (optional)", false));
        Parameters.Add(new ToolParameter("priority", "Priority 1-4 where 1 is highest (optional)", false));
        Parameters.Add(new ToolParameter("tags", "Comma-separated tags to apply (optional)", false));
        Parameters.Add(new ToolParameter("area_path", "Area path (optional, defaults to project root)", false));
        Parameters.Add(new ToolParameter("iteration_path", "Iteration/sprint path (optional)", false));
        Parameters.Add(new ToolParameter("parent_id", "ID of parent work item to link to (optional)", false));
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
                return JsonSerializer.Serialize(new { success = false, error = "Azure DevOps is not configured. Please set the server URL and PAT in Settings → Integrations → Azure DevOps." });
            }

            var project = GetArg(arguments, "project") ?? _devOpsOptions.AzureDevOps.DefaultProject;
            var type = GetArg(arguments, "type");
            var title = GetArg(arguments, "title");

            if (string.IsNullOrWhiteSpace(project)) return JsonSerializer.Serialize(new { success = false, error = "Project name is required." });
            if (string.IsNullOrWhiteSpace(type)) return JsonSerializer.Serialize(new { success = false, error = "Work item type is required." });
            if (string.IsNullOrWhiteSpace(title)) return JsonSerializer.Serialize(new { success = false, error = "Title is required." });

            var description = GetArg(arguments, "description");
            var assignedTo = GetArg(arguments, "assigned_to");
            var tags = GetArg(arguments, "tags");
            var areaPath = GetArg(arguments, "area_path");
            var iterationPath = GetArg(arguments, "iteration_path");
            var parentId = GetArg(arguments, "parent_id");
            var priority = GetArg(arguments, "priority");

            // Build JSON Patch document
            var ops = new List<object>
            {
                Op("/fields/System.Title", title)
            };

            if (!string.IsNullOrWhiteSpace(description))
                ops.Add(Op("/fields/System.Description", description));
            if (!string.IsNullOrWhiteSpace(assignedTo))
                ops.Add(Op("/fields/System.AssignedTo", assignedTo));
            if (!string.IsNullOrWhiteSpace(tags))
                ops.Add(Op("/fields/System.Tags", tags));
            if (!string.IsNullOrWhiteSpace(areaPath))
                ops.Add(Op("/fields/System.AreaPath", areaPath));
            if (!string.IsNullOrWhiteSpace(iterationPath))
                ops.Add(Op("/fields/System.IterationPath", iterationPath));
            if (!string.IsNullOrWhiteSpace(priority) && int.TryParse(priority, out var priorityInt))
                ops.Add(Op("/fields/Microsoft.VSTS.Common.Priority", priorityInt));
            if (!string.IsNullOrWhiteSpace(parentId) && int.TryParse(parentId, out var parentIdInt))
                ops.Add(new { op = "add", path = "/relations/-", value = new { rel = "System.LinkTypes.Hierarchy-Reverse", url = $"{BuildBaseUrl(adoGw)}/_apis/wit/workItems/{parentIdInt}" } });

            var baseUrl = BuildBaseUrl(adoGw);
            var url = $"{baseUrl}/{Uri.EscapeDataString(project)}/_apis/wit/workitems/${Uri.EscapeDataString(type)}?api-version=6.0";

            Logger.LogInformation("Creating ADO work item type={Type} in project={Project}", type, project);

            using var client = CreateAuthenticatedClient(adoGw.AccessToken);
            var json = JsonSerializer.Serialize(ops);
            var content = new StringContent(json, Encoding.UTF8, "application/json-patch+json");
            var resp = await client.PostAsync(url, content, cancellationToken);

            var body = await resp.Content.ReadAsStringAsync(cancellationToken);
            if (!resp.IsSuccessStatusCode)
                return JsonSerializer.Serialize(new { success = false, error = $"ADO API returned {(int)resp.StatusCode}: {Truncate(body, 400)}" });

            var result = JsonSerializer.Deserialize<JsonElement>(body);
            var id = result.TryGetProperty("id", out var idProp) ? idProp.GetInt32() : 0;
            var webUrl = result.TryGetProperty("_links", out var links) && links.TryGetProperty("html", out var html) && html.TryGetProperty("href", out var href)
                ? href.GetString() : null;

            return JsonSerializer.Serialize(new
            {
                success = true,
                id,
                type,
                title,
                project,
                url = webUrl,
                message = $"Work item #{id} '{title}' created successfully."
            });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error creating ADO work item");
            return JsonSerializer.Serialize(new { success = false, error = ex.Message });
        }
    }

    private static object Op(string path, object value) => new { op = "add", path, value };
    private static string? GetArg(IDictionary<string, object?> args, string key) =>
        args.TryGetValue(key, out var v) ? v?.ToString() : null;

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
        client.Timeout = TimeSpan.FromSeconds(20);
        var encoded = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{accessToken}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", encoded);
        return client;
    }

    private static string Truncate(string s, int max) => s.Length > max ? s[..max] + "..." : s;
}
