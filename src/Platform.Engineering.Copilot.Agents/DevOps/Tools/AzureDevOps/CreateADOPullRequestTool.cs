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
/// Tool for creating a pull request in an Azure DevOps Git repository.
/// Supports title, description, source/target branch, reviewers, work item links, and draft mode.
/// </summary>
public class CreateADOPullRequestTool : BaseTool
{
    public override string Name => "create_ado_pull_request";

    public override string Description =>
        "Create a pull request in an Azure DevOps Git repository. " +
        "Specify the source branch, target branch, title, and optional description, reviewers, and draft mode. " +
        "Use to submit code changes for review or initiate a merge workflow.";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GatewayOptions _gatewayOptions;
    private readonly DevOpsAgentOptions _devOpsOptions;

    public CreateADOPullRequestTool(
        ILogger<CreateADOPullRequestTool> logger,
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
        Parameters.Add(new ToolParameter("title", "Pull request title", true));
        Parameters.Add(new ToolParameter("source_branch", "Source branch name (the branch with your changes)", true));
        Parameters.Add(new ToolParameter("target_branch", "Target branch to merge into (default: main)", false));
        Parameters.Add(new ToolParameter("description", "Pull request description in Markdown", false));
        Parameters.Add(new ToolParameter("reviewers", "Comma-separated list of reviewer email addresses or display names", false));
        Parameters.Add(new ToolParameter("draft", "Set to true to create as draft (default: false)", false));
        Parameters.Add(new ToolParameter("work_items", "Comma-separated work item IDs to link to this PR", false));
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
            var title = GetArg(arguments, "title") ?? "";
            var sourceBranch = GetArg(arguments, "source_branch") ?? "";
            var targetBranch = GetArg(arguments, "target_branch") ?? "main";
            var description = GetArg(arguments, "description") ?? "";
            var reviewersRaw = GetArg(arguments, "reviewers");
            var isDraft = string.Equals(GetArg(arguments, "draft"), "true", StringComparison.OrdinalIgnoreCase);
            var workItemsRaw = GetArg(arguments, "work_items");

            if (string.IsNullOrWhiteSpace(project)) return JsonSerializer.Serialize(new { success = false, error = "project is required." });
            if (string.IsNullOrWhiteSpace(repo)) return JsonSerializer.Serialize(new { success = false, error = "repository is required." });
            if (string.IsNullOrWhiteSpace(title)) return JsonSerializer.Serialize(new { success = false, error = "title is required." });
            if (string.IsNullOrWhiteSpace(sourceBranch)) return JsonSerializer.Serialize(new { success = false, error = "source_branch is required." });

            var baseUrl = BuildBaseUrl(adoGw);
            using var client = CreateAuthenticatedClient(adoGw.AccessToken);

            // Build reviewer list (must resolve to identity IDs — send display names as-is for now)
            var reviewers = reviewersRaw?
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(r => new { id = r }) // ADO accepts unique names/email in id field for simple cases
                .ToArray() ?? Array.Empty<object>();

            // Build work item refs
            var workItemRefs = workItemsRaw?
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(id => new { id = int.TryParse(id, out var i) ? i : 0 })
                .Where(x => x.id > 0)
                .ToArray() ?? Array.Empty<object>();

            var body = JsonSerializer.Serialize(new
            {
                title,
                description,
                sourceRefName = $"refs/heads/{sourceBranch.TrimStart('/')}",
                targetRefName = $"refs/heads/{targetBranch.TrimStart('/')}",
                isDraft,
                reviewers,
                workItemRefs
            });

            var url = $"{baseUrl}/{Uri.EscapeDataString(project)}/_apis/git/repositories/{Uri.EscapeDataString(repo)}/pullrequests?api-version=6.0";
            Logger.LogInformation("Creating ADO PR in {Project}/{Repo}: {Title}", project, repo, title);

            var content = new StringContent(body, Encoding.UTF8, "application/json");
            var resp = await client.PostAsync(url, content, cancellationToken);
            var respBody = await resp.Content.ReadAsStringAsync(cancellationToken);

            if (!resp.IsSuccessStatusCode)
                return JsonSerializer.Serialize(new { success = false, error = $"ADO API {(int)resp.StatusCode}: {Truncate(respBody, 400)}" });

            var result = JsonSerializer.Deserialize<JsonElement>(respBody);
            return JsonSerializer.Serialize(new
            {
                success = true,
                id = result.TryGetProperty("pullRequestId", out var id) ? id.GetInt32() : 0,
                title,
                sourceBranch,
                targetBranch,
                isDraft,
                status = result.TryGetProperty("status", out var s) ? s.GetString() : null,
                url = result.TryGetProperty("url", out var u) ? u.GetString() : null,
                message = $"Pull request '{title}' created successfully."
            });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error creating ADO pull request");
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
