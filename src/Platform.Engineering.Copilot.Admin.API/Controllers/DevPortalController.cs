using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Platform.Engineering.Copilot.Core.Configuration;
using Platform.Engineering.Copilot.Core.Interfaces.GitHub;

namespace Platform.Engineering.Copilot.Admin.API.Controllers;

/// <summary>
/// Developer Portal API — connects GitHub and Azure DevOps for seamless integration
/// of repositories, work items (issues/boards), pipelines, and artifacts.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class DevPortalController : ControllerBase
{
    private readonly IGitHubServices? _gitHubService;
    private readonly GatewayOptions _gatewayOptions;
    private readonly ILogger<DevPortalController> _logger;

    // In-memory connection store — replace with DB persistence when entities are ready
    private static readonly List<DevOpsConnectionDto> _connections = new();
    private static readonly object _lock = new();

    public DevPortalController(
        ILogger<DevPortalController> logger,
        IOptions<GatewayOptions> gatewayOptions,
        IGitHubServices? gitHubService = null)
    {
        _logger = logger;
        _gatewayOptions = gatewayOptions.Value;
        _gitHubService = gitHubService;
    }

    #region Connections

    /// <summary>
    /// Get developer portal summary with all connections and stats.
    /// </summary>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(DevPortalSummaryDto), StatusCodes.Status200OK)]
    public ActionResult<DevPortalSummaryDto> GetSummary()
    {
        List<DevOpsConnectionDto> connections;
        lock (_lock)
        {
            connections = _connections.ToList();
        }

        // Auto-detect from gateway config if no connections registered
        if (connections.Count == 0)
        {
            if (_gatewayOptions.GitHub?.Enabled == true)
            {
                connections.Add(new DevOpsConnectionDto
                {
                    Id = "auto-github",
                    Name = "GitHub (Gateway Config)",
                    Provider = "GitHub",
                    ServerUrl = _gatewayOptions.GitHub.ApiBaseUrl ?? "https://api.github.com",
                    Organization = _gatewayOptions.GitHub.DefaultOwner,
                    Status = "Connected",
                    ConnectedAt = DateTime.UtcNow
                });
            }

            if (_gatewayOptions.AzureDevOps?.Enabled == true)
            {
                connections.Add(new DevOpsConnectionDto
                {
                    Id = "auto-ado",
                    Name = "Azure DevOps (Gateway Config)",
                    Provider = "AzureDevOps",
                    ServerUrl = _gatewayOptions.AzureDevOps.ServerUrl ?? "",
                    Organization = ExtractAdoOrg(_gatewayOptions.AzureDevOps.ServerUrl),
                    Status = "Connected",
                    ConnectedAt = DateTime.UtcNow
                });
            }
        }

        var summary = new DevPortalSummaryDto
        {
            TotalConnections = connections.Count,
            ActiveConnections = connections.Count(c => c.Status == "Connected"),
            TotalRepositories = connections.Sum(c => c.RepositoryCount),
            OpenWorkItems = connections.Sum(c => c.WorkItemCount),
            ActivePipelines = connections.Sum(c => c.PipelineCount),
            Connections = connections
        };

        return Ok(summary);
    }

    /// <summary>
    /// Get all registered DevOps connections.
    /// </summary>
    [HttpGet("connections")]
    [ProducesResponseType(typeof(List<DevOpsConnectionDto>), StatusCodes.Status200OK)]
    public ActionResult<List<DevOpsConnectionDto>> GetConnections()
    {
        List<DevOpsConnectionDto> connections;
        lock (_lock)
        {
            connections = _connections.ToList();
        }
        return Ok(connections);
    }

    /// <summary>
    /// Create a new DevOps connection (GitHub or Azure DevOps).
    /// </summary>
    [HttpPost("connections")]
    [ProducesResponseType(typeof(DevOpsConnectionDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<DevOpsConnectionDto> CreateConnection([FromBody] CreateConnectionDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Provider))
            return BadRequest(new { error = "Name and Provider are required." });

        if (request.Provider != "GitHub" && request.Provider != "AzureDevOps" && request.Provider != "AdoServer")
            return BadRequest(new { error = "Provider must be 'GitHub', 'AzureDevOps', or 'AdoServer'." });

        var defaultUrl = request.Provider switch
        {
            "GitHub" => "https://api.github.com",
            "AzureDevOps" => "https://dev.azure.com",
            "AdoServer" => "", // on-prem — must be provided
            _ => ""
        };

        if (request.Provider == "AdoServer" && string.IsNullOrWhiteSpace(request.ServerUrl))
            return BadRequest(new { error = "Server URL is required for ADO Server connections." });

        var connection = new DevOpsConnectionDto
        {
            Id = Guid.NewGuid().ToString(),
            Name = request.Name,
            Provider = request.Provider,
            ServerUrl = request.ServerUrl ?? defaultUrl,
            Organization = request.Organization,
            Project = request.Project,
            Collection = request.Collection,
            Status = "Connected",
            ConnectedAt = DateTime.UtcNow,
            ConnectedBy = "admin"
        };

        lock (_lock)
        {
            _connections.Add(connection);
        }

        _logger.LogInformation("DevPortal connection created: {Name} ({Provider})", connection.Name, connection.Provider);
        return CreatedAtAction(nameof(GetConnections), new { id = connection.Id }, connection);
    }

    /// <summary>
    /// Test connectivity of a DevOps connection.
    /// </summary>
    [HttpPost("connections/test")]
    [ProducesResponseType(typeof(ConnectionTestDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ConnectionTestDto>> TestConnection([FromBody] CreateConnectionDto request)
    {
        if (request.Provider == "GitHub")
        {
            if (_gitHubService == null)
                return Ok(new ConnectionTestDto { Success = false, Message = "GitHub service is not configured in gateway settings." });

            try
            {
                var repos = await _gitHubService.ListRepositoriesAsync(request.Organization);
                var repoCount = repos?.Count() ?? 0;
                return Ok(new ConnectionTestDto
                {
                    Success = true,
                    Message = $"Successfully connected to GitHub. Found {repoCount} repositories.",
                    Organization = request.Organization,
                    RepositoryCount = repoCount
                });
            }
            catch (Exception ex)
            {
                return Ok(new ConnectionTestDto { Success = false, Message = $"GitHub connection failed: {ex.Message}" });
            }
        }

        if (request.Provider == "AzureDevOps")
        {
            // ADO cloud connectivity test — validate config is present
            var hasToken = _gatewayOptions.AzureDevOps?.Enabled == true
                           && !string.IsNullOrEmpty(_gatewayOptions.AzureDevOps.AccessToken);
            return Ok(new ConnectionTestDto
            {
                Success = hasToken,
                Message = hasToken
                    ? $"Azure DevOps configuration detected for {_gatewayOptions.AzureDevOps?.ServerUrl}"
                    : "Azure DevOps token is not configured. Set Gateway:AzureDevOps in appsettings.",
                Organization = request.Organization
            });
        }

        if (request.Provider == "AdoServer")
        {
            // ADO Server (on-prem) connectivity test
            if (string.IsNullOrWhiteSpace(request.ServerUrl))
                return Ok(new ConnectionTestDto { Success = false, Message = "Server URL is required for ADO Server." });

            try
            {
                using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                // Build the API URL: {serverUrl}/{collection}/_apis/projects?api-version=6.0
                var baseUrl = request.ServerUrl!.TrimEnd('/');
                var collection = request.Collection ?? "DefaultCollection";
                var apiUrl = $"{baseUrl}/{collection}/_apis/projects?api-version=6.0";

                var httpRequest = new HttpRequestMessage(HttpMethod.Get, apiUrl);
                // ADO Server PAT auth uses Basic with empty username
                if (!string.IsNullOrEmpty(_gatewayOptions.AzureDevOps?.AccessToken))
                {
                    var encodedPat = Convert.ToBase64String(
                        System.Text.Encoding.ASCII.GetBytes($":{_gatewayOptions.AzureDevOps.AccessToken}"));
                    httpRequest.Headers.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", encodedPat);
                }

                var response = await httpClient.SendAsync(httpRequest);
                if (response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    return Ok(new ConnectionTestDto
                    {
                        Success = true,
                        Message = $"Successfully connected to ADO Server at {baseUrl}/{collection}.",
                        Organization = request.Organization
                    });
                }
                else
                {
                    return Ok(new ConnectionTestDto
                    {
                        Success = false,
                        Message = $"ADO Server returned {response.StatusCode}. Verify URL, collection, and PAT."
                    });
                }
            }
            catch (Exception ex)
            {
                return Ok(new ConnectionTestDto
                {
                    Success = false,
                    Message = $"Cannot reach ADO Server: {ex.Message}"
                });
            }
        }

        return Ok(new ConnectionTestDto { Success = false, Message = "Unknown provider." });
    }

    /// <summary>
    /// Delete a DevOps connection.
    /// </summary>
    [HttpDelete("connections/{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult DeleteConnection(string id)
    {
        lock (_lock)
        {
            var removed = _connections.RemoveAll(c => c.Id == id);
            if (removed == 0)
                return NotFound(new { error = $"Connection {id} not found." });
        }

        _logger.LogInformation("DevPortal connection deleted: {Id}", id);
        return NoContent();
    }

    /// <summary>
    /// Sync a connection — refresh repository/work-item/pipeline counts from the provider.
    /// </summary>
    [HttpPost("connections/{id}/sync")]
    [ProducesResponseType(typeof(SyncResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SyncResultDto>> SyncConnection(string id)
    {
        DevOpsConnectionDto? connection;
        lock (_lock)
        {
            connection = _connections.FirstOrDefault(c => c.Id == id);
        }
        if (connection == null)
            return NotFound(new { error = $"Connection {id} not found." });

        if (connection.Provider == "GitHub" && _gitHubService != null)
        {
            try
            {
                var repos = await _gitHubService.ListRepositoriesAsync(connection.Organization);
                var repoList = repos?.ToList() ?? new();
                connection.RepositoryCount = repoList.Count;
                connection.LastSyncedAt = DateTime.UtcNow;
                connection.Status = "Connected";

                return Ok(new SyncResultDto
                {
                    Success = true,
                    Message = $"Synced {repoList.Count} repositories from GitHub.",
                    ItemsSynced = repoList.Count
                });
            }
            catch (Exception ex)
            {
                connection.Status = "Error";
                return Ok(new SyncResultDto { Success = false, Message = ex.Message });
            }
        }

        connection.LastSyncedAt = DateTime.UtcNow;
        return Ok(new SyncResultDto { Success = true, Message = "Sync completed (metadata only).", ItemsSynced = 0 });
    }

    #endregion

    #region Repositories

    /// <summary>
    /// List repositories for a connection (or all connections).
    /// </summary>
    [HttpGet("repositories")]
    [ProducesResponseType(typeof(List<DevPortalRepoDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<DevPortalRepoDto>>> GetRepositories(
        [FromQuery] string? connectionId = null,
        [FromQuery] string? provider = null)
    {
        var repos = new List<DevPortalRepoDto>();

        // GitHub repos via gateway service
        if (_gitHubService != null && (provider == null || provider == "GitHub"))
        {
            try
            {
                var owner = _gatewayOptions.GitHub?.DefaultOwner;
                var ghRepos = await _gitHubService.ListRepositoriesAsync(owner);
                if (ghRepos != null)
                {
                    repos.AddRange(ghRepos.Select(r => new DevPortalRepoDto
                    {
                        Id = r.Id.ToString(),
                        ConnectionId = connectionId ?? "auto-github",
                        Provider = "GitHub",
                        Name = r.Name,
                        FullName = r.FullName,
                        Description = r.Description,
                        DefaultBranch = r.DefaultBranch ?? "main",
                        Url = r.HtmlUrl,
                        CloneUrl = r.CloneUrl,
                        Language = r.Language,
                        IsPrivate = r.Private,
                        OpenIssueCount = r.OpenIssuesCount,
                        Stars = r.StargazersCount,
                        LastPushAt = r.PushedAt?.UtcDateTime,
                        CreatedAt = r.CreatedAt.UtcDateTime
                    }));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch GitHub repositories");
            }
        }

        return Ok(repos);
    }

    #endregion

    #region Work Items / Issues

    /// <summary>
    /// List work items / issues across connections.
    /// </summary>
    [HttpGet("workitems")]
    [ProducesResponseType(typeof(List<DevPortalWorkItemDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<DevPortalWorkItemDto>>> GetWorkItems(
        [FromQuery] string? connectionId = null,
        [FromQuery] string? provider = null,
        [FromQuery] string? repository = null,
        [FromQuery] string? state = null)
    {
        var items = new List<DevPortalWorkItemDto>();

        if (_gitHubService != null && (provider == null || provider == "GitHub"))
        {
            try
            {
                var owner = _gatewayOptions.GitHub?.DefaultOwner;
                if (!string.IsNullOrEmpty(owner) && !string.IsNullOrEmpty(repository))
                {
                    var issues = await _gitHubService.ListIssuesAsync(owner, repository, state ?? "open");
                    if (issues != null)
                    {
                        items.AddRange(issues.Select(i => new DevPortalWorkItemDto
                        {
                            Id = i.Id.ToString(),
                            ConnectionId = connectionId ?? "auto-github",
                            Provider = "GitHub",
                            Type = i.PullRequest != null ? "PullRequest" : "Issue",
                            Title = i.Title,
                            Description = i.Body,
                            State = i.State.StringValue,
                            AssignedTo = i.Assignee?.Login,
                            Labels = i.Labels?.Select(l => l.Name).ToList() ?? new(),
                            RepositoryName = repository,
                            Url = i.HtmlUrl,
                            Number = i.Number,
                            CreatedAt = i.CreatedAt.UtcDateTime,
                            UpdatedAt = i.UpdatedAt?.UtcDateTime,
                            ClosedAt = i.ClosedAt?.UtcDateTime
                        }));
                    }
                }
                else if (!string.IsNullOrEmpty(owner))
                {
                    // Get issues from top repos
                    var repos = await _gitHubService.ListRepositoriesAsync(owner);
                    if (repos != null)
                    {
                        foreach (var repo in repos.Take(10))
                        {
                            try
                            {
                                var issues = await _gitHubService.ListIssuesAsync(owner, repo.Name, state ?? "open");
                                if (issues != null)
                                {
                                    items.AddRange(issues.Select(i => new DevPortalWorkItemDto
                                    {
                                        Id = i.Id.ToString(),
                                        ConnectionId = connectionId ?? "auto-github",
                                        Provider = "GitHub",
                                        Type = i.PullRequest != null ? "PullRequest" : "Issue",
                                        Title = i.Title,
                                        Description = i.Body,
                                        State = i.State.StringValue,
                                        AssignedTo = i.Assignee?.Login,
                                        Labels = i.Labels?.Select(l => l.Name).ToList() ?? new(),
                                        RepositoryName = repo.Name,
                                        Url = i.HtmlUrl,
                                        Number = i.Number,
                                        CreatedAt = i.CreatedAt.UtcDateTime,
                                        UpdatedAt = i.UpdatedAt?.UtcDateTime,
                                        ClosedAt = i.ClosedAt?.UtcDateTime
                                    }));
                                }
                            }
                            catch { /* skip repos with no issues access */ }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch GitHub issues");
            }
        }

        return Ok(items);
    }

    #endregion

    #region Pipelines / Actions

    /// <summary>
    /// List pipelines / GitHub Actions across connections.
    /// </summary>
    [HttpGet("pipelines")]
    [ProducesResponseType(typeof(List<DevPortalPipelineDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<DevPortalPipelineDto>>> GetPipelines(
        [FromQuery] string? connectionId = null,
        [FromQuery] string? provider = null,
        [FromQuery] string? repository = null)
    {
        var pipelines = new List<DevPortalPipelineDto>();

        if (_gitHubService != null && (provider == null || provider == "GitHub"))
        {
            try
            {
                var owner = _gatewayOptions.GitHub?.DefaultOwner;
                if (!string.IsNullOrEmpty(owner) && !string.IsNullOrEmpty(repository))
                {
                    var runs = await _gitHubService.ListWorkflowRunsAsync(owner, repository);
                    if (runs != null)
                    {
                        pipelines.AddRange(runs.Select(r => new DevPortalPipelineDto
                        {
                            Id = r.Id.ToString(),
                            ConnectionId = connectionId ?? "auto-github",
                            Provider = "GitHub",
                            Name = r.Name,
                            RepositoryName = repository,
                            Status = r.Status.StringValue,
                            Conclusion = r.Conclusion?.StringValue,
                            Branch = r.HeadBranch,
                            Url = r.HtmlUrl,
                            LastRunAt = r.CreatedAt.UtcDateTime,
                            TriggerEvent = r.Event
                        }));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch GitHub Actions runs");
            }
        }

        return Ok(pipelines);
    }

    #endregion

    #region Helper DTOs & Utilities

    private static string? ExtractAdoOrg(string? serverUrl)
    {
        if (string.IsNullOrEmpty(serverUrl)) return null;
        try
        {
            var uri = new Uri(serverUrl);
            var segments = uri.AbsolutePath.Trim('/').Split('/');
            return segments.Length > 0 ? segments[0] : null;
        }
        catch { return null; }
    }

    #endregion
}

#region DTOs

public class DevOpsConnectionDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string ServerUrl { get; set; } = string.Empty;
    public string? Organization { get; set; }
    public string? Project { get; set; }
    public string? Collection { get; set; }
    public string Status { get; set; } = "Disconnected";
    public DateTime? ConnectedAt { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public string? ConnectedBy { get; set; }
    public int RepositoryCount { get; set; }
    public int WorkItemCount { get; set; }
    public int PipelineCount { get; set; }
}

public class CreateConnectionDto
{
    public string Name { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string? ServerUrl { get; set; }
    public string? Organization { get; set; }
    public string? Project { get; set; }
    public string? Collection { get; set; }
}

public class ConnectionTestDto
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? Organization { get; set; }
    public string? UserName { get; set; }
    public int RepositoryCount { get; set; }
}

public class DevPortalSummaryDto
{
    public int TotalConnections { get; set; }
    public int ActiveConnections { get; set; }
    public int TotalRepositories { get; set; }
    public int OpenWorkItems { get; set; }
    public int ActivePipelines { get; set; }
    public int RecentDeployments { get; set; }
    public List<DevOpsConnectionDto> Connections { get; set; } = new();
}

public class DevPortalRepoDto
{
    public string Id { get; set; } = string.Empty;
    public string ConnectionId { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string DefaultBranch { get; set; } = "main";
    public string Url { get; set; } = string.Empty;
    public string CloneUrl { get; set; } = string.Empty;
    public string? Language { get; set; }
    public bool IsPrivate { get; set; }
    public int OpenIssueCount { get; set; }
    public int OpenPrCount { get; set; }
    public int Stars { get; set; }
    public DateTime? LastPushAt { get; set; }
    public DateTime? CreatedAt { get; set; }
}

public class DevPortalWorkItemDto
{
    public string Id { get; set; } = string.Empty;
    public string ConnectionId { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string State { get; set; } = string.Empty;
    public string? AssignedTo { get; set; }
    public string? Priority { get; set; }
    public List<string> Labels { get; set; } = new();
    public string RepositoryName { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public int Number { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
}

public class DevPortalPipelineDto
{
    public string Id { get; set; } = string.Empty;
    public string ConnectionId { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string RepositoryName { get; set; } = string.Empty;
    public string? Status { get; set; }
    public string? Conclusion { get; set; }
    public string? Branch { get; set; }
    public string Url { get; set; } = string.Empty;
    public DateTime? LastRunAt { get; set; }
    public int? DurationSeconds { get; set; }
    public string? TriggerEvent { get; set; }
}

public class SyncResultDto
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public int ItemsSynced { get; set; }
    public List<string>? Errors { get; set; }
}

#endregion
