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
/// Tool for listing Azure DevOps Pipelines (build and release) in a project.
/// </summary>
public class ListADOPipelinesTool : BaseTool
{
    public override string Name => "list_ado_pipelines";

    public override string Description =>
        "List Azure DevOps build pipelines in a project. " +
        "Returns pipeline names, IDs, folders, and recent run status. " +
        "Use this to discover CI/CD pipelines, check pipeline health, or find a pipeline to trigger.";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GatewayOptions _gatewayOptions;
    private readonly DevOpsAgentOptions _devOpsOptions;

    public ListADOPipelinesTool(
        ILogger<ListADOPipelinesTool> logger,
        IHttpClientFactory httpClientFactory,
        IOptions<GatewayOptions> gatewayOptions,
        IOptions<DevOpsAgentOptions> devOpsOptions)
        : base(logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _gatewayOptions = gatewayOptions?.Value ?? throw new ArgumentNullException(nameof(gatewayOptions));
        _devOpsOptions = devOpsOptions?.Value ?? throw new ArgumentNullException(nameof(devOpsOptions));

        Parameters.Add(new ToolParameter("project", "Project name to list pipelines from", true));
        Parameters.Add(new ToolParameter("limit", "Maximum number of pipelines to return (default: 50)", false));
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
            if (string.IsNullOrWhiteSpace(project))
                return JsonSerializer.Serialize(new { success = false, error = "Project name is required." });

            var limit = 50;
            if (arguments.TryGetValue("limit", out var limitVal) && int.TryParse(limitVal?.ToString(), out var lp)) limit = Math.Min(lp, 200);

            var baseUrl = BuildBaseUrl(adoGw);
            var url = $"{baseUrl}/{Uri.EscapeDataString(project)}/_apis/pipelines?api-version=6.0&$top={limit}";

            Logger.LogInformation("Listing ADO pipelines for project {Project}", project);

            using var client = CreateAuthenticatedClient(adoGw.AccessToken);
            var resp = await client.GetAsync(url, cancellationToken);

            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync(cancellationToken);
                return JsonSerializer.Serialize(new { success = false, error = $"ADO API returned {(int)resp.StatusCode}: {Truncate(err, 400)}" });
            }

            var json = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            var pipelines = new List<object>();

            if (json.TryGetProperty("value", out var items))
            {
                foreach (var p in items.EnumerateArray())
                {
                    pipelines.Add(new
                    {
                        id = p.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
                        name = p.TryGetProperty("name", out var name) ? name.GetString() : null,
                        folder = p.TryGetProperty("folder", out var folder) ? folder.GetString() : null,
                        revision = p.TryGetProperty("revision", out var rev) ? rev.GetInt32() : 0,
                        url = p.TryGetProperty("url", out var u) ? u.GetString() : null,
                        webUrl = p.TryGetProperty("_links", out var links) && links.TryGetProperty("web", out var web) && web.TryGetProperty("href", out var href) ? href.GetString() : null
                    });
                }
            }

            return JsonSerializer.Serialize(new { success = true, project, count = pipelines.Count, pipelines });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error listing ADO pipelines");
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
