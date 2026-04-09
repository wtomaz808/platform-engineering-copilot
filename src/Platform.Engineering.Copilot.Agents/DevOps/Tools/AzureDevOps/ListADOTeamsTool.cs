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
/// Tool for listing teams in an Azure DevOps project, including member counts.
/// Parallel to ListGitHubTeamsTool.
/// </summary>
public class ListADOTeamsTool : BaseTool
{
    public override string Name => "list_ado_teams";

    public override string Description =>
        "List all teams in an Azure DevOps project. " +
        "Returns team names, descriptions, and member counts. " +
        "Use to discover team structure, find the right team to add a member to, or audit team organization.";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GatewayOptions _gatewayOptions;
    private readonly DevOpsAgentOptions _devOpsOptions;

    public ListADOTeamsTool(
        ILogger<ListADOTeamsTool> logger,
        IHttpClientFactory httpClientFactory,
        IOptions<GatewayOptions> gatewayOptions,
        IOptions<DevOpsAgentOptions> devOpsOptions)
        : base(logger)
    {
        _httpClientFactory = httpClientFactory;
        _gatewayOptions = gatewayOptions?.Value ?? new GatewayOptions();
        _devOpsOptions = devOpsOptions?.Value ?? new DevOpsAgentOptions();

        Parameters.Add(new ToolParameter("project", "Project name to list teams for", true));
        Parameters.Add(new ToolParameter("include_members", "Set to true to also return team member counts (default: false — slightly slower)", false));
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
            var includeMembers = string.Equals(GetArg(arguments, "include_members"), "true", StringComparison.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(project)) return JsonSerializer.Serialize(new { success = false, error = "project is required." });

            var baseUrl = BuildBaseUrl(adoGw);
            using var client = CreateAuthenticatedClient(adoGw.AccessToken);

            var url = $"{baseUrl}/_apis/projects/{Uri.EscapeDataString(project)}/teams?api-version=6.0";
            var resp = await client.GetAsync(url, cancellationToken);
            var body = await resp.Content.ReadAsStringAsync(cancellationToken);

            if (!resp.IsSuccessStatusCode)
                return JsonSerializer.Serialize(new { success = false, error = $"ADO API {(int)resp.StatusCode}: {Truncate(body, 300)}" });

            var json = JsonSerializer.Deserialize<JsonElement>(body);
            var teams = json.TryGetProperty("value", out var v) ? v : default;

            var results = new List<object>();
            if (teams.ValueKind == JsonValueKind.Array)
            {
                foreach (var team in teams.EnumerateArray())
                {
                    var teamId = team.TryGetProperty("id", out var id) ? id.GetString() : null;
                    var teamName = team.TryGetProperty("name", out var n) ? n.GetString() : null;

                    int? memberCount = null;
                    if (includeMembers && !string.IsNullOrWhiteSpace(teamId))
                    {
                        var membersUrl = $"{baseUrl}/_apis/projects/{Uri.EscapeDataString(project)}/teams/{teamId}/members?api-version=6.0";
                        var mResp = await client.GetAsync(membersUrl, cancellationToken);
                        if (mResp.IsSuccessStatusCode)
                        {
                            var mJson = JsonSerializer.Deserialize<JsonElement>(await mResp.Content.ReadAsStringAsync(cancellationToken));
                            if (mJson.TryGetProperty("count", out var cnt)) memberCount = cnt.GetInt32();
                        }
                    }

                    results.Add(new
                    {
                        id = teamId,
                        name = teamName,
                        description = team.TryGetProperty("description", out var desc) ? desc.GetString() : null,
                        url = team.TryGetProperty("url", out var u) ? u.GetString() : null,
                        memberCount
                    });
                }
            }

            return JsonSerializer.Serialize(new { success = true, project, count = results.Count, teams = results });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error listing ADO teams");
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
