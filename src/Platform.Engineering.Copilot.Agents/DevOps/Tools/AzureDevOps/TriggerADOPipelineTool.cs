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
/// Tool for triggering an Azure DevOps pipeline run.
/// </summary>
public class TriggerADOPipelineTool : BaseTool
{
    public override string Name => "trigger_ado_pipeline";

    public override string Description =>
        "Trigger (run) an Azure DevOps pipeline by pipeline ID or name. " +
        "Optionally specify a branch and pipeline variables. " +
        "Use this to kick off CI builds, deployments, or any automated pipeline.";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GatewayOptions _gatewayOptions;
    private readonly DevOpsAgentOptions _devOpsOptions;

    public TriggerADOPipelineTool(
        ILogger<TriggerADOPipelineTool> logger,
        IHttpClientFactory httpClientFactory,
        IOptions<GatewayOptions> gatewayOptions,
        IOptions<DevOpsAgentOptions> devOpsOptions)
        : base(logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _gatewayOptions = gatewayOptions?.Value ?? throw new ArgumentNullException(nameof(gatewayOptions));
        _devOpsOptions = devOpsOptions?.Value ?? throw new ArgumentNullException(nameof(devOpsOptions));

        Parameters.Add(new ToolParameter("project", "Project name containing the pipeline", true));
        Parameters.Add(new ToolParameter("pipeline_id", "Numeric pipeline ID to trigger (use list_ado_pipelines to find it)", true));
        Parameters.Add(new ToolParameter("branch", "Branch/ref to run on (e.g. main, refs/heads/develop). Defaults to the pipeline default branch.", false));
        Parameters.Add(new ToolParameter("variables", "JSON object of pipeline variables to override, e.g. {\"ENV\":\"staging\"} (optional)", false));
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

            var project = GetArg(arguments, "project") ?? _devOpsOptions.AzureDevOps.DefaultProject;
            if (string.IsNullOrWhiteSpace(project))
                return JsonSerializer.Serialize(new { success = false, error = "Project name is required." });

            var pipelineIdStr = GetArg(arguments, "pipeline_id");
            if (!int.TryParse(pipelineIdStr, out var pipelineId) || pipelineId <= 0)
                return JsonSerializer.Serialize(new { success = false, error = "Valid pipeline ID is required. Use list_ado_pipelines to find the pipeline ID." });

            var branch = GetArg(arguments, "branch");
            var variablesStr = GetArg(arguments, "variables");

            // Build run request body
            var runRequest = new Dictionary<string, object>();

            if (!string.IsNullOrWhiteSpace(branch))
            {
                var refName = branch.StartsWith("refs/") ? branch : $"refs/heads/{branch}";
                runRequest["resources"] = new { repositories = new { self = new { refName } } };
            }

            if (!string.IsNullOrWhiteSpace(variablesStr))
            {
                try
                {
                    var vars = JsonSerializer.Deserialize<Dictionary<string, string>>(variablesStr);
                    if (vars != null)
                    {
                        var adoVars = vars.ToDictionary(kv => kv.Key, kv => (object)new { value = kv.Value });
                        runRequest["variables"] = adoVars;
                    }
                }
                catch
                {
                    // Ignore bad variable JSON
                }
            }

            var baseUrl = BuildBaseUrl(adoGw);
            var url = $"{baseUrl}/{Uri.EscapeDataString(project)}/_apis/pipelines/{pipelineId}/runs?api-version=6.0";

            Logger.LogInformation("Triggering ADO pipeline {PipelineId} in project {Project} on branch {Branch}", pipelineId, project, branch ?? "default");

            using var client = CreateAuthenticatedClient(adoGw.AccessToken);
            var json = JsonSerializer.Serialize(runRequest);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var resp = await client.PostAsync(url, content, cancellationToken);

            var body = await resp.Content.ReadAsStringAsync(cancellationToken);
            if (!resp.IsSuccessStatusCode)
                return JsonSerializer.Serialize(new { success = false, error = $"ADO API returned {(int)resp.StatusCode}: {Truncate(body, 400)}" });

            var result = JsonSerializer.Deserialize<JsonElement>(body);
            var runId = result.TryGetProperty("id", out var idProp) ? idProp.GetInt32() : 0;
            var state = result.TryGetProperty("state", out var stateProp) ? stateProp.GetString() : null;
            var webUrl = result.TryGetProperty("_links", out var links) && links.TryGetProperty("web", out var web) && web.TryGetProperty("href", out var href)
                ? href.GetString() : null;

            return JsonSerializer.Serialize(new
            {
                success = true,
                pipelineId,
                runId,
                project,
                state,
                url = webUrl,
                message = $"Pipeline #{pipelineId} triggered. Run #{runId} is {state}."
            });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error triggering ADO pipeline");
            return JsonSerializer.Serialize(new { success = false, error = ex.Message });
        }
    }

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
