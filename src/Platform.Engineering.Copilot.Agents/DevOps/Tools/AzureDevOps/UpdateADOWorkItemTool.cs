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
/// Tool for updating Azure DevOps work items — change state, reassign, update title/description, add comments.
/// </summary>
public class UpdateADOWorkItemTool : BaseTool
{
    public override string Name => "update_ado_work_item";

    public override string Description =>
        "Update an existing Azure DevOps work item. Can change state (e.g. Active → Resolved), " +
        "reassign to a different user, update title, description, priority, or add a comment. " +
        "Use this to close bugs, move tasks to Done, or update acceptance criteria.";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GatewayOptions _gatewayOptions;

    public UpdateADOWorkItemTool(
        ILogger<UpdateADOWorkItemTool> logger,
        IHttpClientFactory httpClientFactory,
        IOptions<GatewayOptions> gatewayOptions)
        : base(logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _gatewayOptions = gatewayOptions?.Value ?? throw new ArgumentNullException(nameof(gatewayOptions));

        Parameters.Add(new ToolParameter("id", "Work item ID to update", true));
        Parameters.Add(new ToolParameter("state", "New state: Active, New, Resolved, Closed, Done (optional)", false));
        Parameters.Add(new ToolParameter("title", "New title (optional)", false));
        Parameters.Add(new ToolParameter("description", "New description (optional)", false));
        Parameters.Add(new ToolParameter("assigned_to", "Display name of user to assign to, or empty string to unassign (optional)", false));
        Parameters.Add(new ToolParameter("priority", "New priority 1-4 (optional)", false));
        Parameters.Add(new ToolParameter("tags", "New comma-separated tags — replaces existing tags (optional)", false));
        Parameters.Add(new ToolParameter("comment", "Comment to add to the work item discussion (optional)", false));
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
                return JsonSerializer.Serialize(new { success = false, error = "Azure DevOps is not configured." });
            }

            var idStr = GetArg(arguments, "id");
            if (!int.TryParse(idStr, out var workItemId) || workItemId <= 0)
                return JsonSerializer.Serialize(new { success = false, error = "Valid work item ID is required." });

            var ops = new List<object>();

            var state = GetArg(arguments, "state");
            if (!string.IsNullOrWhiteSpace(state)) ops.Add(Op("/fields/System.State", state));

            var title = GetArg(arguments, "title");
            if (!string.IsNullOrWhiteSpace(title)) ops.Add(Op("/fields/System.Title", title));

            var description = GetArg(arguments, "description");
            if (!string.IsNullOrWhiteSpace(description)) ops.Add(Op("/fields/System.Description", description));

            if (arguments.ContainsKey("assigned_to")) ops.Add(Op("/fields/System.AssignedTo", GetArg(arguments, "assigned_to") ?? ""));

            var priority = GetArg(arguments, "priority");
            if (!string.IsNullOrWhiteSpace(priority) && int.TryParse(priority, out var pInt)) ops.Add(Op("/fields/Microsoft.VSTS.Common.Priority", pInt));

            var tags = GetArg(arguments, "tags");
            if (tags != null) ops.Add(Op("/fields/System.Tags", tags));

            var comment = GetArg(arguments, "comment");
            if (!string.IsNullOrWhiteSpace(comment)) ops.Add(Op("/fields/System.History", comment));

            if (ops.Count == 0)
                return JsonSerializer.Serialize(new { success = false, error = "No fields specified to update." });

            var baseUrl = BuildBaseUrl(adoGw);
            var url = $"{baseUrl}/_apis/wit/workitems/{workItemId}?api-version=6.0";

            Logger.LogInformation("Updating ADO work item #{Id}", workItemId);

            using var client = CreateAuthenticatedClient(adoGw.AccessToken);
            var json = JsonSerializer.Serialize(ops);
            var content = new StringContent(json, Encoding.UTF8, "application/json-patch+json");
            var request = new HttpRequestMessage(new HttpMethod("PATCH"), url) { Content = content };
            var resp = await client.SendAsync(request, cancellationToken);

            var body = await resp.Content.ReadAsStringAsync(cancellationToken);
            if (!resp.IsSuccessStatusCode)
                return JsonSerializer.Serialize(new { success = false, error = $"ADO API returned {(int)resp.StatusCode}: {Truncate(body, 400)}" });

            return JsonSerializer.Serialize(new
            {
                success = true,
                id = workItemId,
                message = $"Work item #{workItemId} updated successfully.",
                updatedFields = ops.Count
            });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error updating ADO work item");
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
