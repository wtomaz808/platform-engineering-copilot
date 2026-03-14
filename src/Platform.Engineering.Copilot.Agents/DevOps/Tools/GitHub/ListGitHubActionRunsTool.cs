using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.Engineering.Copilot.Agents.Common;
using Platform.Engineering.Copilot.Agents.DevOps.Configuration;
using Platform.Engineering.Copilot.Core.Configuration;
using System.Text.Json;
using System.Web;

namespace Platform.Engineering.Copilot.Agents.DevOps.Tools.GitHub;

/// <summary>
/// Tool for listing GitHub Actions workflow runs with filtering and status information.
/// Uses GitHub REST API v3 GET /repos/{owner}/{repo}/actions/runs endpoint.
/// </summary>
public class ListGitHubActionRunsTool : BaseTool
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GatewayOptions _gatewayOptions;
    private readonly DevOpsAgentOptions _devOpsOptions;

    public override string Name => "list_github_action_runs";

    public override string Description =>
        "Lists GitHub Actions workflow runs with filtering by workflow, branch, status, and conclusion. " +
        "Use this to check CI/CD pipeline status, find failed deployments, or audit workflow history.";

    public ListGitHubActionRunsTool(
        ILogger<ListGitHubActionRunsTool> logger,
        IHttpClientFactory httpClientFactory,
        IOptions<GatewayOptions> gatewayOptions,
        IOptions<DevOpsAgentOptions> devOpsOptions)
        : base(logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _gatewayOptions = gatewayOptions?.Value ?? throw new ArgumentNullException(nameof(gatewayOptions));
        _devOpsOptions = devOpsOptions?.Value ?? throw new ArgumentNullException(nameof(devOpsOptions));

        Parameters.Add(new ToolParameter("repository", "Repository identifier in format 'owner/repo' (e.g., 'azure/azure-sdk')", true));
        Parameters.Add(new ToolParameter("workflowId", "OPTIONAL: Filter by workflow ID or filename (e.g., 'deploy.yml')", false));
        Parameters.Add(new ToolParameter("branch", "OPTIONAL: Filter by branch name", false));
        Parameters.Add(new ToolParameter("actor", "OPTIONAL: Filter by actor (GitHub username who triggered the run)", false));
        Parameters.Add(new ToolParameter("status", "OPTIONAL: Filter by status - 'queued', 'in_progress', 'completed'", false));
        Parameters.Add(new ToolParameter("conclusion", "OPTIONAL: Filter by conclusion - 'success', 'failure', 'cancelled', 'skipped', 'neutral'", false));
        Parameters.Add(new ToolParameter("maxResults", "OPTIONAL: Maximum number of runs to return (default: 30, max: 100)", false));
    }

    public override async Task<string> ExecuteAsync(
        IDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var repository = arguments.TryGetValue("repository", out var repoVal) ? repoVal?.ToString() ?? "" : "";
        var workflowId = arguments.TryGetValue("workflowId", out var wfVal) ? wfVal?.ToString() : null;
        var branch = arguments.TryGetValue("branch", out var branchVal) ? branchVal?.ToString() : null;
        var actor = arguments.TryGetValue("actor", out var actorVal) ? actorVal?.ToString() : null;
        var status = arguments.TryGetValue("status", out var statusVal) ? statusVal?.ToString() : null;
        var conclusion = arguments.TryGetValue("conclusion", out var conclusionVal) ? conclusionVal?.ToString() : null;
        int? maxResults = arguments.TryGetValue("maxResults", out var maxVal) && int.TryParse(maxVal?.ToString(), out var maxInt) ? maxInt : 30;

        try
        {
            // Validate repository format
            var parts = repository.Split('/');
            if (parts.Length != 2)
            {
                return CreateErrorResponse("Repository must be in format 'owner/repo'");
            }

            var owner = parts[0];
            var repo = parts[1];

            // Validate status if provided
            var validStatuses = new[] { "queued", "in_progress", "completed" };
            if (!string.IsNullOrEmpty(status) && !validStatuses.Contains(status.ToLower()))
            {
                return CreateErrorResponse($"Status must be one of: {string.Join(", ", validStatuses)}");
            }

            // Validate conclusion if provided
            var validConclusions = new[] { "success", "failure", "cancelled", "skipped", "neutral", "timed_out", "action_required" };
            if (!string.IsNullOrEmpty(conclusion) && !validConclusions.Contains(conclusion.ToLower()))
            {
                return CreateErrorResponse($"Conclusion must be one of: {string.Join(", ", validConclusions)}");
            }

            // Validate and limit maxResults
            var perPage = Math.Min(maxResults ?? 30, 100);

            var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Add("Authorization", $"token {_gatewayOptions.GitHub.AccessToken}");
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Platform-Engineering-Copilot");
            httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");

            // Build URL - filter by workflow if specified
            string url;
            if (!string.IsNullOrEmpty(workflowId))
            {
                url = $"https://api.github.com/repos/{owner}/{repo}/actions/workflows/{workflowId}/runs";
            }
            else
            {
                url = $"https://api.github.com/repos/{owner}/{repo}/actions/runs";
            }

            // Build query parameters
            var queryParams = HttpUtility.ParseQueryString(string.Empty);
            queryParams["per_page"] = perPage.ToString();

            if (!string.IsNullOrEmpty(branch))
            {
                queryParams["branch"] = branch;
            }

            if (!string.IsNullOrEmpty(actor))
            {
                queryParams["actor"] = actor;
            }

            if (!string.IsNullOrEmpty(status))
            {
                queryParams["status"] = status.ToLower();
            }

            url += $"?{queryParams}";

            var response = await httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                return CreateErrorResponse($"Failed to list workflow runs: {response.StatusCode} - {errorContent}");
            }

            var content = await response.Content.ReadAsStringAsync();
            var data = JsonSerializer.Deserialize<JsonElement>(content);

            IEnumerable<JsonElement> runs = data.GetProperty("workflow_runs").EnumerateArray();

            // Filter by conclusion if specified (API doesn't support this as query param)
            if (!string.IsNullOrEmpty(conclusion))
            {
                runs = runs.Where(r => 
                    r.TryGetProperty("conclusion", out var concl) && 
                    concl.ValueKind != JsonValueKind.Null &&
                    concl.GetString()?.Equals(conclusion, StringComparison.OrdinalIgnoreCase) == true);
            }

            var runList = runs.Select(run => new
            {
                id = run.GetProperty("id").GetInt64(),
                runNumber = run.GetProperty("run_number").GetInt32(),
                workflowId = run.GetProperty("workflow_id").GetInt64(),
                workflowName = run.GetProperty("name").GetString(),
                headBranch = run.GetProperty("head_branch").GetString(),
                status = run.GetProperty("status").GetString(),
                conclusion = run.TryGetProperty("conclusion", out var concl) && concl.ValueKind != JsonValueKind.Null
                    ? concl.GetString()
                    : null,
                actor = run.GetProperty("actor").GetProperty("login").GetString(),
                eventType = run.GetProperty("event").GetString(),
                htmlUrl = run.GetProperty("html_url").GetString(),
                createdAt = run.GetProperty("created_at").GetString(),
                updatedAt = run.GetProperty("updated_at").GetString(),
                runStartedAt = run.TryGetProperty("run_started_at", out var started) && started.ValueKind != JsonValueKind.Null
                    ? started.GetString()
                    : null
            }).ToArray();

            var result = new
            {
                message = "Workflow runs retrieved successfully",
                repository,
                filters = new
                {
                    workflowId,
                    branch,
                    actor,
                    status,
                    conclusion
                },
                totalCount = data.GetProperty("total_count").GetInt32(),
                returnedCount = runList.Length,
                runs = runList
            };

            return CreateSuccessResponse(result);
        }
        catch (Exception ex)
        {
            return CreateErrorResponse($"Error listing workflow runs: {ex.Message}");
        }
    }
}
