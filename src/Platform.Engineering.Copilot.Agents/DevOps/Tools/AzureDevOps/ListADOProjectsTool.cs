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
/// Tool for listing Azure DevOps projects.
/// </summary>
public class ListADOProjectsTool : BaseTool
{
    public override string Name => "list_ado_projects";

    public override string Description =>
        "List projects in an Azure DevOps Server or Azure DevOps Services instance. " +
        "Use this to discover existing projects, check what projects exist, or verify connectivity.";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GatewayOptions _gatewayOptions;
    private readonly DevOpsAgentOptions _devOpsOptions;

    public ListADOProjectsTool(
        ILogger<ListADOProjectsTool> logger,
        IHttpClientFactory httpClientFactory,
        IOptions<GatewayOptions> gatewayOptions,
        IOptions<DevOpsAgentOptions> devOpsOptions)
        : base(logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _gatewayOptions = gatewayOptions?.Value ?? throw new ArgumentNullException(nameof(gatewayOptions));
        _devOpsOptions = devOpsOptions?.Value ?? throw new ArgumentNullException(nameof(devOpsOptions));

        Parameters.Add(new ToolParameter("limit", "Maximum number of projects to return (default: 50)", false));
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

            var limit = arguments.TryGetValue("limit", out var limitValue) && limitValue is int limitInt ? limitInt : 50;

            var baseUrl = adoGw.ServerUrl.TrimEnd('/');
            if (!string.IsNullOrWhiteSpace(adoGw.DefaultCollection))
            {
                baseUrl = $"{baseUrl}/{adoGw.DefaultCollection.Trim('/')}";
            }

            var apiUrl = $"{baseUrl}/_apis/projects?api-version=5.0&$top={limit}";

            Logger.LogInformation("Listing ADO projects from {Url}", apiUrl);

            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15);
            var encoded = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{adoGw.AccessToken}"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", encoded);

            var resp = await client.GetAsync(apiUrl, cancellationToken);
            if (!resp.IsSuccessStatusCode)
            {
                var errorBody = await resp.Content.ReadAsStringAsync(cancellationToken);
                Logger.LogWarning("ADO projects API returned {Status}: {Body}", resp.StatusCode, errorBody);
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = $"Azure DevOps API returned {(int)resp.StatusCode}: {(errorBody.Length > 300 ? errorBody[..300] : errorBody)}"
                });
            }

            var json = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            var projects = new List<object>();

            if (json.TryGetProperty("value", out var value))
            {
                foreach (var p in value.EnumerateArray())
                {
                    projects.Add(new
                    {
                        name = p.GetProperty("name").GetString(),
                        id = p.GetProperty("id").GetString(),
                        description = p.TryGetProperty("description", out var desc) ? desc.GetString() : null,
                        state = p.TryGetProperty("state", out var st) ? st.GetString() : null,
                        url = p.TryGetProperty("url", out var u) ? u.GetString() : null
                    });
                }
            }

            Logger.LogInformation("✅ Found {Count} ADO projects", projects.Count);

            return JsonSerializer.Serialize(new
            {
                success = true,
                count = projects.Count,
                server = adoGw.ServerUrl,
                collection = adoGw.DefaultCollection ?? "(default)",
                projects
            });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error listing ADO projects");
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = ex.Message
            });
        }
    }
}
