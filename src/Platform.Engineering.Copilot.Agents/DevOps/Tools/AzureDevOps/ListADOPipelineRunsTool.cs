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
/// Tool for listing Azure DevOps pipeline runs (build history) for a specific pipeline.
/// Parallel to ListGitHubActionRunsTool.
/// </summary>
public class ListADOPipelineRunsTool : BaseTool
{
    public override string Name => "list_ado_pipeline_runs";

    public override string Description =>
        "List recent runs for an Azure DevOps pipeline. Shows run status, result, branch, trigger, and duration. " +
        "Use to check build history, find failed runs, or verify recent deployment status. " +
        "Requires the pipeline ID or name.";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GatewayOptions _gatewayOptions;
    private readonly DevOpsAgentOptions _devOpsOptions;

    public ListADOPipelineRunsTool(
        ILogger<ListADOPipelineRunsTool> logger,
        IHttpClientFactory httpClientFactory,
        IOptions<GatewayOptions> gatewayOptions,
        IOptions<DevOpsAgentOptions> devOpsOptions)
        : base(logger)
    {
        _httpClientFactory = httpClientFactory;
        _gatewayOptions = gatewayOptions?.Value ?? new GatewayOptions();
        _devOpsOptions = devOpsOptions?.Value ?? new DevOpsAgentOptions();

        Parameters.Add(new ToolParameter("project", "Project name", true));
        Parameters.Add(new ToolParameter("pipeline_id", "Pipeline ID (integer). Use list_ado_pipelines to find the ID.", true));
        Parameters.Add(new ToolParameter("limit", "Maximum number of runs to return (default: 10, max: 50)", false));
        Parameters.Add(new ToolParameter("branch", "Filter runs by branch name (optional)", false));
    }

    public override async Task<string> ExecuteAsync(
        IDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var adoGw = _gatewayOptions.AzureDevOps;
            if (!adoGw.Enabled || string.IsNullOrWhiteSpace(adoGw.AccessToken) || string.IsNullOrWhiteSpace(adoGw.ServerUrl))
                return JsonSerializer.Serialize(new { success = false, error = "Azure DevOps is not configured." });

            var project = GetArg(arguments, "project") ?? _devOpsOptions.AzureDevOps.DefaultProject;
            var pipelineIdStr = GetArg(arguments, "pipeline_id") ?? "";
            var limit = int.TryParse(GetArg(arguments, "limit"), out var l) ? Math.Min(l, 50) : 10;
            var branch = GetArg(arguments, "branch");

            if (string.IsNullOrWhiteSpace(project)) return JsonSerializer.Serialize(new { success = false, error = "project is required." });
            if (!int.TryParse(pipelineIdStr, out var pipelineId))
                return JsonSerializer.Serialize(new { success = false, error = "pipeline_id must be a valid integer. Use list_ado_pipelines to find the ID." });

            var baseUrl = BuildBaseUrl(adoGw);
            using var client = CreateAuthenticatedClient(adoGw.AccessToken);

            var url = $"{baseUrl}/{Uri.EscapeDataString(project)}/_apis/pipelines/{pipelineId}/runs?$top={limit}&api-version=6.0";
            var resp = await client.GetAsync(url, cancellationToken);
            var body = await resp.Content.ReadAsStringAsync(cancellationToken);

            if (!resp.IsSuccessStatusCode)
                return JsonSerializer.Serialize(new { success = false, error = $"ADO API {(int)resp.StatusCode}: {Truncate(body, 300)}" });

            var json = JsonSerializer.Deserialize<JsonElement>(body);
            var runs = json.TryGetProperty("value", out var v) ? v : default;

            var results = new List<object>();
            if (runs.ValueKind == JsonValueKind.Array)
            {
                foreach (var run in runs.EnumerateArray())
                {
                    var runBranch = run.TryGetProperty("resources", out var res) &&
                                    res.TryGetProperty("repositories", out var repos) &&
                                    repos.TryGetProperty("self", out var self) &&
                                    self.TryGetProperty("refName", out var refName)
                        ? refName.GetString()?.Replace("refs/heads/", "")
                        : null;

                    if (!string.IsNullOrWhiteSpace(branch) &&
                        !string.Equals(runBranch, branch, StringComparison.OrdinalIgnoreCase))
                        continue;

                    results.Add(new
                    {
                        runId = run.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
                        name = run.TryGetProperty("name", out var name) ? name.GetString() : null,
                        state = run.TryGetProperty("state", out var state) ? state.GetString() : null,
                        result = run.TryGetProperty("result", out var result) ? result.GetString() : null,
                        branch = runBranch,
                        createdDate = run.TryGetProperty("createdDate", out var cd) ? cd.GetString() : null,
                        finishedDate = run.TryGetProperty("finishedDate", out var fd) ? fd.GetString() : null,
                        url = run.TryGetProperty("url", out var u) ? u.GetString() : null,
                        pipelineId
                    });
                }
            }

            return JsonSerializer.Serialize(new
            {
                success = true,
                project,
                pipelineId,
                count = results.Count,
                runs = results
            });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error listing ADO pipeline runs");
            return JsonSerializer.Serialize(new { success = false, error = ex.Message });
        }
    }

    private static string? GetArg(IDictionary<string, object?> args, string key) =>
        args.TryGetValue(key, out var v) ? v?.ToString() : null;

    private string BuildBaseUrl(AzureDevOpsGatewayOptions adoGw)
    {
        var url = adoGw.ServerUrl!.TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(adoGw.DefaultCollection))
            url += $"/{adoGw.DefaultCollection.Trim('/')}";
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
