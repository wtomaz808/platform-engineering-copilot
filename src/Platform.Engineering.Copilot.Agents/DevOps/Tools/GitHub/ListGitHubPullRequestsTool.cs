using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.Engineering.Copilot.Agents.Common;
using Platform.Engineering.Copilot.Agents.DevOps.Configuration;
using Platform.Engineering.Copilot.Core.Configuration;
using System.Text.Json;
using System.Web;

namespace Platform.Engineering.Copilot.Agents.DevOps.Tools.GitHub;

/// <summary>
/// Tool for listing GitHub pull requests with filtering by state, author, base branch, and sorting.
/// Uses GitHub REST API v3 GET /repos/{owner}/{repo}/pulls endpoint.
/// </summary>
public class ListGitHubPullRequestsTool : BaseTool
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GatewayOptions _gatewayOptions;
    private readonly DevOpsAgentOptions _devOpsOptions;

    public override string Name => "list_github_pull_requests";

    public override string Description =>
        "Lists GitHub pull requests with filtering by state, author, base branch, and sorting options. " +
        "Use this to find open PRs, review outstanding requests, or check merge status.";

    public ListGitHubPullRequestsTool(
        ILogger<ListGitHubPullRequestsTool> logger,
        IHttpClientFactory httpClientFactory,
        IOptions<GatewayOptions> gatewayOptions,
        IOptions<DevOpsAgentOptions> devOpsOptions)
        : base(logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _gatewayOptions = gatewayOptions?.Value ?? throw new ArgumentNullException(nameof(gatewayOptions));
        _devOpsOptions = devOpsOptions?.Value ?? throw new ArgumentNullException(nameof(devOpsOptions));

        Parameters.Add(new ToolParameter("repository", "Repository identifier in format 'owner/repo' (e.g., 'azure/azure-sdk')", true));
        Parameters.Add(new ToolParameter("state", "OPTIONAL: Filter by PR state - 'open', 'closed', or 'all' (default: 'open')", false));
        Parameters.Add(new ToolParameter("baseBranch", "OPTIONAL: Filter by base branch the PRs are targeting", false));
        Parameters.Add(new ToolParameter("headBranch", "OPTIONAL: Filter by head branch with changes", false));
        Parameters.Add(new ToolParameter("sort", "OPTIONAL: Sort by 'created', 'updated', 'popularity', or 'long-running' (default: 'created')", false));
        Parameters.Add(new ToolParameter("direction", "OPTIONAL: Sort direction - 'asc' or 'desc' (default: 'desc')", false));
        Parameters.Add(new ToolParameter("maxResults", "OPTIONAL: Maximum number of pull requests to return (default: 30, max: 100)", false));
    }

    public override async Task<string> ExecuteAsync(
        IDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var repository = arguments.TryGetValue("repository", out var repoVal) ? repoVal?.ToString() ?? "" : "";
        var state = arguments.TryGetValue("state", out var stateVal) ? stateVal?.ToString() : "open";
        var baseBranch = arguments.TryGetValue("baseBranch", out var baseVal) ? baseVal?.ToString() : null;
        var headBranch = arguments.TryGetValue("headBranch", out var headVal) ? headVal?.ToString() : null;
        var sort = arguments.TryGetValue("sort", out var sortVal) ? sortVal?.ToString() : "created";
        var direction = arguments.TryGetValue("direction", out var dirVal) ? dirVal?.ToString() : "desc";
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

            // Validate state
            var validStates = new[] { "open", "closed", "all" };
            if (!string.IsNullOrEmpty(state) && !validStates.Contains(state.ToLower()))
            {
                return CreateErrorResponse($"State must be one of: {string.Join(", ", validStates)}");
            }

            // Validate sort
            var validSorts = new[] { "created", "updated", "popularity", "long-running" };
            if (!string.IsNullOrEmpty(sort) && !validSorts.Contains(sort.ToLower()))
            {
                return CreateErrorResponse($"Sort must be one of: {string.Join(", ", validSorts)}");
            }

            // Validate direction
            var validDirections = new[] { "asc", "desc" };
            if (!string.IsNullOrEmpty(direction) && !validDirections.Contains(direction.ToLower()))
            {
                return CreateErrorResponse($"Direction must be one of: {string.Join(", ", validDirections)}");
            }

            // Validate and limit maxResults
            var perPage = Math.Min(maxResults ?? 30, 100);

            var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Add("Authorization", $"token {_gatewayOptions.GitHub.AccessToken}");
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Platform-Engineering-Copilot");
            httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");

            // Build query parameters
            var queryParams = HttpUtility.ParseQueryString(string.Empty);
            queryParams["state"] = state?.ToLower() ?? "open";
            queryParams["sort"] = sort?.ToLower() ?? "created";
            queryParams["direction"] = direction?.ToLower() ?? "desc";
            queryParams["per_page"] = perPage.ToString();

            if (!string.IsNullOrEmpty(baseBranch))
            {
                queryParams["base"] = baseBranch;
            }

            if (!string.IsNullOrEmpty(headBranch))
            {
                queryParams["head"] = headBranch;
            }

            var url = $"https://api.github.com/repos/{owner}/{repo}/pulls?{queryParams}";
            var response = await httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                return CreateErrorResponse($"Failed to list pull requests: {response.StatusCode} - {errorContent}");
            }

            var content = await response.Content.ReadAsStringAsync();
            var prs = JsonSerializer.Deserialize<JsonElement>(content);

            var prList = prs.EnumerateArray()
                .Select(pr => new
                {
                    number = pr.GetProperty("number").GetInt32(),
                    title = pr.GetProperty("title").GetString(),
                    state = pr.GetProperty("state").GetString(),
                    draft = pr.GetProperty("draft").GetBoolean(),
                    htmlUrl = pr.GetProperty("html_url").GetString(),
                    user = pr.GetProperty("user").GetProperty("login").GetString(),
                    sourceBranch = pr.GetProperty("head").GetProperty("ref").GetString(),
                    targetBranch = pr.GetProperty("base").GetProperty("ref").GetString(),
                    labels = pr.TryGetProperty("labels", out var lbls)
                        ? lbls.EnumerateArray().Select(l => l.GetProperty("name").GetString()).ToArray()
                        : Array.Empty<string>(),
                    requestedReviewers = pr.TryGetProperty("requested_reviewers", out var rvwrs)
                        ? rvwrs.EnumerateArray().Select(r => r.GetProperty("login").GetString()).ToArray()
                        : Array.Empty<string>(),
                    createdAt = pr.GetProperty("created_at").GetString(),
                    updatedAt = pr.GetProperty("updated_at").GetString(),
                    mergedAt = pr.TryGetProperty("merged_at", out var merged) && merged.ValueKind != JsonValueKind.Null
                        ? merged.GetString()
                        : null,
                    mergeable = pr.TryGetProperty("mergeable", out var mgbl) && mgbl.ValueKind != JsonValueKind.Null
                        ? mgbl.GetBoolean().ToString()
                        : "unknown"
                })
                .ToArray();

            var result = new
            {
                message = "Pull requests retrieved successfully",
                repository,
                filters = new
                {
                    state = state ?? "open",
                    baseBranch,
                    headBranch,
                    sort = sort ?? "created",
                    direction = direction ?? "desc"
                },
                totalCount = prList.Length,
                pullRequests = prList
            };

            return CreateSuccessResponse(result);
        }
        catch (Exception ex)
        {
            return CreateErrorResponse($"Error listing pull requests: {ex.Message}");
        }
    }
}
