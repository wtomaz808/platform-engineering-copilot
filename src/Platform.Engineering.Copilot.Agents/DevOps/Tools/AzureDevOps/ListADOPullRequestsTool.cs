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
/// Tool for listing pull requests in an Azure DevOps Git repository.
/// Supports filtering by status, target branch, creator, and reviewer.
/// </summary>
public class ListADOPullRequestsTool : BaseTool
{
    public override string Name => "list_ado_pull_requests";

    public override string Description =>
        "List pull requests in an Azure DevOps Git repository. " +
        "Filter by status (active, completed, abandoned), target branch, creator, or reviewer. " +
        "Use to review open PRs, check merge status, or audit code review activity.";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GatewayOptions _gatewayOptions;
    private readonly DevOpsAgentOptions _devOpsOptions;

    public ListADOPullRequestsTool(
        ILogger<ListADOPullRequestsTool> logger,
        IHttpClientFactory httpClientFactory,
        IOptions<GatewayOptions> gatewayOptions,
        IOptions<DevOpsAgentOptions> devOpsOptions)
        : base(logger)
    {
        _httpClientFactory = httpClientFactory;
        _gatewayOptions = gatewayOptions?.Value ?? new GatewayOptions();
        _devOpsOptions = devOpsOptions?.Value ?? new DevOpsAgentOptions();

        Parameters.Add(new ToolParameter("project", "Project name", true));
        Parameters.Add(new ToolParameter("repository", "Repository name", true));
        Parameters.Add(new ToolParameter("status", "Filter by status: active, completed, abandoned, all (default: active)", false));
        Parameters.Add(new ToolParameter("target_branch", "Filter by target branch name (e.g. main, develop)", false));
        Parameters.Add(new ToolParameter("creator", "Filter by creator display name or email", false));
        Parameters.Add(new ToolParameter("limit", "Maximum results to return (default: 25, max: 100)", false));
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
            var repo = GetArg(arguments, "repository") ?? "";
            var status = GetArg(arguments, "status") ?? "active";
            var targetBranch = GetArg(arguments, "target_branch");
            var creator = GetArg(arguments, "creator");
            var limit = int.TryParse(GetArg(arguments, "limit"), out var l) ? Math.Min(l, 100) : 25;

            if (string.IsNullOrWhiteSpace(project)) return JsonSerializer.Serialize(new { success = false, error = "project is required." });
            if (string.IsNullOrWhiteSpace(repo)) return JsonSerializer.Serialize(new { success = false, error = "repository is required." });

            var baseUrl = BuildBaseUrl(adoGw);
            using var client = CreateAuthenticatedClient(adoGw.AccessToken);

            var url = $"{baseUrl}/{Uri.EscapeDataString(project)}/_apis/git/repositories/{Uri.EscapeDataString(repo)}/pullrequests" +
                      $"?searchCriteria.status={status}&$top={limit}&api-version=6.0";
            if (!string.IsNullOrWhiteSpace(targetBranch))
                url += $"&searchCriteria.targetRefName=refs/heads/{Uri.EscapeDataString(targetBranch)}";

            var resp = await client.GetAsync(url, cancellationToken);
            var body = await resp.Content.ReadAsStringAsync(cancellationToken);
            if (!resp.IsSuccessStatusCode)
                return JsonSerializer.Serialize(new { success = false, error = $"ADO API {(int)resp.StatusCode}: {Truncate(body, 300)}" });

            var json = JsonSerializer.Deserialize<JsonElement>(body);
            var prs = json.TryGetProperty("value", out var v) ? v : default;

            var results = new List<object>();
            if (prs.ValueKind == JsonValueKind.Array)
            {
                foreach (var pr in prs.EnumerateArray())
                {
                    var creatorName = pr.TryGetProperty("createdBy", out var cb)
                        ? cb.TryGetProperty("displayName", out var dn) ? dn.GetString() : null : null;

                    if (!string.IsNullOrWhiteSpace(creator) &&
                        !string.Equals(creatorName, creator, StringComparison.OrdinalIgnoreCase))
                        continue;

                    results.Add(new
                    {
                        id = pr.TryGetProperty("pullRequestId", out var id) ? id.GetInt32() : 0,
                        title = pr.TryGetProperty("title", out var t) ? t.GetString() : null,
                        status = pr.TryGetProperty("status", out var s) ? s.GetString() : null,
                        sourceBranch = pr.TryGetProperty("sourceRefName", out var src) ? src.GetString()?.Replace("refs/heads/", "") : null,
                        targetBranch = pr.TryGetProperty("targetRefName", out var tgt) ? tgt.GetString()?.Replace("refs/heads/", "") : null,
                        createdBy = creatorName,
                        createdDate = pr.TryGetProperty("creationDate", out var cd) ? cd.GetString() : null,
                        url = pr.TryGetProperty("url", out var u) ? u.GetString() : null,
                        isDraft = pr.TryGetProperty("isDraft", out var dr) && dr.GetBoolean(),
                        reviewerCount = pr.TryGetProperty("reviewers", out var rv) ? rv.GetArrayLength() : 0
                    });
                }
            }

            return JsonSerializer.Serialize(new { success = true, project, repository = repo, status, count = results.Count, pullRequests = results });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error listing ADO pull requests");
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
