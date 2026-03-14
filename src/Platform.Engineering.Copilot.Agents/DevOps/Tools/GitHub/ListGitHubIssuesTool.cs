using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.Engineering.Copilot.Agents.Common;
using Platform.Engineering.Copilot.Agents.DevOps.Configuration;
using Platform.Engineering.Copilot.Core.Configuration;
using System.Text.Json;
using System.Web;

namespace Platform.Engineering.Copilot.Agents.DevOps.Tools.GitHub;

/// <summary>
/// Tool for listing and filtering GitHub issues by state, labels, assignees, and other criteria.
/// Uses GitHub REST API v3 GET /repos/{owner}/{repo}/issues endpoint.
/// </summary>
public class ListGitHubIssuesTool : BaseTool
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GatewayOptions _gatewayOptions;
    private readonly DevOpsAgentOptions _devOpsOptions;

    public override string Name => "list_github_issues";

    public override string Description =>
        "Lists GitHub issues with filtering by state, labels, assignees, creator, and sorting options. " +
        "Use this to find open bugs, feature requests, or track work items in a repository.";

    public ListGitHubIssuesTool(
        ILogger<ListGitHubIssuesTool> logger,
        IHttpClientFactory httpClientFactory,
        IOptions<GatewayOptions> gatewayOptions,
        IOptions<DevOpsAgentOptions> devOpsOptions)
        : base(logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _gatewayOptions = gatewayOptions?.Value ?? throw new ArgumentNullException(nameof(gatewayOptions));
        _devOpsOptions = devOpsOptions?.Value ?? throw new ArgumentNullException(nameof(devOpsOptions));

        Parameters.Add(new ToolParameter("repository", "Repository identifier in format 'owner/repo' (e.g., 'azure/azure-sdk')", true));
        Parameters.Add(new ToolParameter("state", "OPTIONAL: Filter by issue state - 'open', 'closed', or 'all' (default: 'open')", false));
        Parameters.Add(new ToolParameter("labels", "OPTIONAL: Comma-separated list of labels to filter by", false));
        Parameters.Add(new ToolParameter("assignee", "OPTIONAL: Filter by assignee username (use 'none' for unassigned, '*' for any assigned)", false));
        Parameters.Add(new ToolParameter("creator", "OPTIONAL: Filter by creator username", false));
        Parameters.Add(new ToolParameter("mentioned", "OPTIONAL: Filter by mentioned username", false));
        Parameters.Add(new ToolParameter("sort", "OPTIONAL: Sort by 'created', 'updated', or 'comments' (default: 'created')", false));
        Parameters.Add(new ToolParameter("direction", "OPTIONAL: Sort direction - 'asc' or 'desc' (default: 'desc')", false));
        Parameters.Add(new ToolParameter("maxResults", "OPTIONAL: Maximum number of issues to return (default: 30, max: 100)", false));
    }

    public override async Task<string> ExecuteAsync(
        IDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var repository = arguments.TryGetValue("repository", out var repoVal) ? repoVal?.ToString() ?? "" : "";
        var state = arguments.TryGetValue("state", out var stateVal) ? stateVal?.ToString() : "open";
        var labels = arguments.TryGetValue("labels", out var labelsVal) ? labelsVal?.ToString() : null;
        var assignee = arguments.TryGetValue("assignee", out var assigneeVal) ? assigneeVal?.ToString() : null;
        var creator = arguments.TryGetValue("creator", out var creatorVal) ? creatorVal?.ToString() : null;
        var mentioned = arguments.TryGetValue("mentioned", out var mentionedVal) ? mentionedVal?.ToString() : null;
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
            var validSorts = new[] { "created", "updated", "comments" };
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

            if (!string.IsNullOrEmpty(labels))
            {
                queryParams["labels"] = labels;
            }

            if (!string.IsNullOrEmpty(assignee))
            {
                queryParams["assignee"] = assignee;
            }

            if (!string.IsNullOrEmpty(creator))
            {
                queryParams["creator"] = creator;
            }

            if (!string.IsNullOrEmpty(mentioned))
            {
                queryParams["mentioned"] = mentioned;
            }

            var url = $"https://api.github.com/repos/{owner}/{repo}/issues?{queryParams}";
            var response = await httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                return CreateErrorResponse($"Failed to list issues: {response.StatusCode} - {errorContent}");
            }

            var content = await response.Content.ReadAsStringAsync();
            var issues = JsonSerializer.Deserialize<JsonElement>(content);

            var issueList = issues.EnumerateArray()
                .Where(i => !i.TryGetProperty("pull_request", out _)) // Exclude pull requests
                .Select(issue => new
                {
                    number = issue.GetProperty("number").GetInt32(),
                    title = issue.GetProperty("title").GetString(),
                    state = issue.GetProperty("state").GetString(),
                    htmlUrl = issue.GetProperty("html_url").GetString(),
                    user = issue.GetProperty("user").GetProperty("login").GetString(),
                    labels = issue.TryGetProperty("labels", out var lbls)
                        ? lbls.EnumerateArray().Select(l => l.GetProperty("name").GetString()).ToArray()
                        : Array.Empty<string>(),
                    assignees = issue.TryGetProperty("assignees", out var assgn)
                        ? assgn.EnumerateArray().Select(a => a.GetProperty("login").GetString()).ToArray()
                        : Array.Empty<string>(),
                    milestone = issue.TryGetProperty("milestone", out var ms) && ms.ValueKind != JsonValueKind.Null
                        ? ms.GetProperty("title").GetString()
                        : null,
                    comments = issue.GetProperty("comments").GetInt32(),
                    createdAt = issue.GetProperty("created_at").GetString(),
                    updatedAt = issue.GetProperty("updated_at").GetString()
                })
                .ToArray();

            var result = new
            {
                message = "Issues retrieved successfully",
                repository,
                filters = new
                {
                    state = state ?? "open",
                    labels,
                    assignee,
                    creator,
                    mentioned,
                    sort = sort ?? "created",
                    direction = direction ?? "desc"
                },
                totalCount = issueList.Length,
                issues = issueList
            };

            return CreateSuccessResponse(result);
        }
        catch (Exception ex)
        {
            return CreateErrorResponse($"Error listing issues: {ex.Message}");
        }
    }
}
