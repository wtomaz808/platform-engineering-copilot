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
/// Tool for listing Azure DevOps repositories with optional project filtering.
/// </summary>
public class ListADORepositoriesTool : BaseTool
{
    public override string Name => "list_ado_repositories";

    public override string Description =>
        "List Git repositories in an Azure DevOps Server or Azure DevOps Services instance. " +
        "Optionally filter by project name. Use this to discover repos, check naming, or find repos.";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GatewayOptions _gatewayOptions;
    private readonly DevOpsAgentOptions _devOpsOptions;

    public ListADORepositoriesTool(
        ILogger<ListADORepositoriesTool> logger,
        IHttpClientFactory httpClientFactory,
        IOptions<GatewayOptions> gatewayOptions,
        IOptions<DevOpsAgentOptions> devOpsOptions)
        : base(logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _gatewayOptions = gatewayOptions?.Value ?? throw new ArgumentNullException(nameof(gatewayOptions));
        _devOpsOptions = devOpsOptions?.Value ?? throw new ArgumentNullException(nameof(devOpsOptions));

        Parameters.Add(new ToolParameter("project", "Project name to list repos for (optional — lists all repos across all projects if omitted)", false));
        Parameters.Add(new ToolParameter("limit", "Maximum number of repositories to return (default: 100)", false));
    }

    public override async Task<string> ExecuteAsync(
        IDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var adoGw = _gatewayOptions.AzureDevOps;

            if (!adoGw.Enabled)
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = "Azure DevOps integration is not enabled. Please configure it in Settings → Integrations → Azure DevOps."
                });
            }

            if (string.IsNullOrWhiteSpace(adoGw.AccessToken))
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = "Azure DevOps access token (PAT) is not configured. Please set it in Settings → Integrations → Azure DevOps."
                });
            }

            if (string.IsNullOrWhiteSpace(adoGw.ServerUrl))
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = "Azure DevOps server URL is not configured."
                });
            }

            var project = arguments.TryGetValue("project", out var projValue) ? projValue?.ToString() : null;
            project ??= _devOpsOptions.AzureDevOps.DefaultProject;

            var limit = arguments.TryGetValue("limit", out var limitValue) && limitValue is int limitInt ? limitInt : 100;

            var baseUrl = adoGw.ServerUrl.TrimEnd('/');
            if (!string.IsNullOrWhiteSpace(adoGw.DefaultCollection))
            {
                baseUrl = $"{baseUrl}/{adoGw.DefaultCollection.Trim('/')}";
            }

            // If project is specified, list repos for that project; otherwise list all repos
            var apiUrl = !string.IsNullOrWhiteSpace(project)
                ? $"{baseUrl}/{Uri.EscapeDataString(project)}/_apis/git/repositories?api-version=5.0"
                : $"{baseUrl}/_apis/git/repositories?api-version=5.0";

            Logger.LogInformation("Listing ADO repositories from {Url}", apiUrl);

            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15);
            var encoded = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{adoGw.AccessToken}"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", encoded);

            var resp = await client.GetAsync(apiUrl, cancellationToken);
            if (!resp.IsSuccessStatusCode)
            {
                var errorBody = await resp.Content.ReadAsStringAsync(cancellationToken);
                Logger.LogWarning("ADO repos API returned {Status}: {Body}", resp.StatusCode, errorBody);
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = $"Azure DevOps API returned {(int)resp.StatusCode}: {(errorBody.Length > 300 ? errorBody[..300] : errorBody)}"
                });
            }

            var json = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            var repos = new List<object>();

            if (json.TryGetProperty("value", out var value))
            {
                var count = 0;
                foreach (var r in value.EnumerateArray())
                {
                    if (count >= limit) break;
                    repos.Add(new
                    {
                        name = r.GetProperty("name").GetString(),
                        id = r.GetProperty("id").GetString(),
                        project = r.TryGetProperty("project", out var proj) && proj.TryGetProperty("name", out var pn) ? pn.GetString() : null,
                        defaultBranch = r.TryGetProperty("defaultBranch", out var db) ? db.GetString() : null,
                        remoteUrl = r.TryGetProperty("remoteUrl", out var ru) ? ru.GetString() : null,
                        webUrl = r.TryGetProperty("webUrl", out var wu) ? wu.GetString() : null,
                        size = r.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0
                    });
                    count++;
                }
            }

            Logger.LogInformation("✅ Found {Count} ADO repositories", repos.Count);

            return JsonSerializer.Serialize(new
            {
                success = true,
                count = repos.Count,
                server = adoGw.ServerUrl,
                collection = adoGw.DefaultCollection ?? "(default)",
                project = project ?? "(all)",
                repositories = repos
            });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error listing ADO repositories");
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = ex.Message
            });
        }
    }
}
