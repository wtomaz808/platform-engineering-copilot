using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Octokit;
using Platform.Engineering.Copilot.Admin.API.DTOs;
using Platform.Engineering.Copilot.Core.Configuration;
using Platform.Engineering.Copilot.Core.Interfaces.GitHub;

namespace Platform.Engineering.Copilot.Admin.API.Controllers;

/// <summary>
/// Developer Portal API — reads integration settings to provide
/// repositories, work items (issues/boards), pipelines, and artifacts
/// from GitHub and Azure DevOps.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class DevPortalController : ControllerBase
{
    private readonly IGitHubServices? _gitHubService;
    private readonly GatewayOptions _gatewayOptions;
    private readonly ILogger<DevPortalController> _logger;

    public DevPortalController(
        ILogger<DevPortalController> logger,
        IOptions<GatewayOptions> gatewayOptions,
        IGitHubServices? gitHubService = null)
    {
        _logger = logger;
        _gatewayOptions = gatewayOptions.Value;
        _gitHubService = gitHubService;
    }

    #region Summary

    /// <summary>
    /// Get developer portal summary with integration status and stats.
    /// </summary>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(DevPortalSummaryDto), StatusCodes.Status200OK)]
    public ActionResult<DevPortalSummaryDto> GetSummary()
    {
        var settings = LoadIntegrationSettings();
        var integrations = new List<IntegrationStatusDto>();

        // GitHub
        var ghEnabled = settings.GitHub?.Enabled == true
                        || _gatewayOptions.GitHub?.Enabled == true;
        integrations.Add(new IntegrationStatusDto
        {
            Provider = "GitHub",
            Enabled = ghEnabled,
            Organization = settings.GitHub?.Organization ?? _gatewayOptions.GitHub?.DefaultOwner
        });

        // Azure DevOps
        var adoEnabled = settings.AzureDevOps?.Enabled == true
                         || _gatewayOptions.AzureDevOps?.Enabled == true;
        integrations.Add(new IntegrationStatusDto
        {
            Provider = settings.AzureDevOps?.ServerType == "server" ? "ADO Server" : "Azure DevOps",
            Enabled = adoEnabled,
            ServerUrl = settings.AzureDevOps?.ServerUrl ?? _gatewayOptions.AzureDevOps?.ServerUrl
        });

        // Azure
        integrations.Add(new IntegrationStatusDto
        {
            Provider = "Azure",
            Enabled = settings.Azure?.Enabled == true
        });

        return Ok(new DevPortalSummaryDto
        {
            EnabledIntegrations = integrations.Count(i => i.Enabled),
            Integrations = integrations
        });
    }

    #endregion

    #region Repositories

    /// <summary>
    /// List repositories from configured integrations.
    /// </summary>
    [HttpGet("repositories")]
    [ProducesResponseType(typeof(List<DevPortalRepoDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<DevPortalRepoDto>>> GetRepositories(
        [FromQuery] string? provider = null)
    {
        var repos = new List<DevPortalRepoDto>();
        var settings = LoadIntegrationSettings();

        // GitHub repos
        if (provider == null || provider == "GitHub")
        {
            repos.AddRange(await FetchGitHubRepos(settings));
        }

        // ADO repos
        if (provider == null || provider == "AzureDevOps" || provider == "AdoServer")
        {
            repos.AddRange(await FetchAdoRepos(settings));
        }

        return Ok(repos);
    }

    #endregion

    #region Work Items / Issues

    /// <summary>
    /// List work items / issues from configured integrations.
    /// </summary>
    [HttpGet("workitems")]
    [ProducesResponseType(typeof(List<DevPortalWorkItemDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<DevPortalWorkItemDto>>> GetWorkItems(
        [FromQuery] string? provider = null,
        [FromQuery] string? repository = null,
        [FromQuery] string? state = null)
    {
        var items = new List<DevPortalWorkItemDto>();
        var settings = LoadIntegrationSettings();

        // GitHub issues
        if (provider == null || provider == "GitHub")
        {
            items.AddRange(await FetchGitHubIssues(settings, repository, state));
        }

        // ADO work items
        if (provider == null || provider == "AzureDevOps" || provider == "AdoServer")
        {
            items.AddRange(await FetchAdoWorkItems(settings, state));
        }

        return Ok(items);
    }

    #endregion

    #region Pipelines / Actions

    /// <summary>
    /// List pipelines / GitHub Actions from configured integrations.
    /// </summary>
    [HttpGet("pipelines")]
    [ProducesResponseType(typeof(List<DevPortalPipelineDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<DevPortalPipelineDto>>> GetPipelines(
        [FromQuery] string? provider = null,
        [FromQuery] string? repository = null)
    {
        var pipelines = new List<DevPortalPipelineDto>();
        var settings = LoadIntegrationSettings();

        // GitHub Actions
        if (provider == null || provider == "GitHub")
        {
            pipelines.AddRange(await FetchGitHubPipelines(settings, repository));
        }

        // ADO pipelines/builds
        if (provider == null || provider == "AzureDevOps" || provider == "AdoServer")
        {
            pipelines.AddRange(await FetchAdoPipelines(settings));
        }

        return Ok(pipelines);
    }

    #endregion

    #region GitHub Helpers

    private async Task<List<DevPortalRepoDto>> FetchGitHubRepos(IntegrationSettingsDto settings)
    {
        var repos = new List<DevPortalRepoDto>();
        var client = CreateGitHubClient(settings);
        if (client == null) return repos;

        var owner = settings.GitHub?.Organization ?? _gatewayOptions.GitHub?.DefaultOwner;
        if (string.IsNullOrEmpty(owner)) return repos;

        try
        {
            IReadOnlyList<Octokit.Repository>? ghRepos;
            try { ghRepos = await client.Repository.GetAllForOrg(owner); }
            catch { ghRepos = await client.Repository.GetAllForUser(owner); }

            if (ghRepos != null)
            {
                repos.AddRange(ghRepos.Select(r => new DevPortalRepoDto
                {
                    Id = r.Id.ToString(),
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
            _logger.LogWarning(ex, "Failed to fetch GitHub repositories for owner {Owner}", owner);
        }
        return repos;
    }

    private async Task<List<DevPortalWorkItemDto>> FetchGitHubIssues(
        IntegrationSettingsDto settings, string? repository, string? state)
    {
        var items = new List<DevPortalWorkItemDto>();
        var client = CreateGitHubClient(settings);
        if (client == null) return items;

        var owner = settings.GitHub?.Organization ?? _gatewayOptions.GitHub?.DefaultOwner;
        if (string.IsNullOrEmpty(owner)) return items;

        var stateFilter = state == "closed" ? ItemStateFilter.Closed : ItemStateFilter.Open;

        try
        {
            if (!string.IsNullOrEmpty(repository))
            {
                // Fetch issues for a specific repo
                var issues = await client.Issue.GetAllForRepository(owner, repository,
                    new RepositoryIssueRequest { State = stateFilter });
                items.AddRange(issues.Select(i => MapIssueToDto(i, repository)));
            }
            else
            {
                // Fetch repos then get issues from each
                IReadOnlyList<Octokit.Repository>? repos;
                try { repos = await client.Repository.GetAllForOrg(owner); }
                catch { repos = await client.Repository.GetAllForUser(owner); }

                if (repos != null)
                {
                    foreach (var repo in repos.Where(r => r.OpenIssuesCount > 0).Take(25))
                    {
                        try
                        {
                            var issues = await client.Issue.GetAllForRepository(owner, repo.Name,
                                new RepositoryIssueRequest { State = stateFilter });
                            items.AddRange(issues.Select(i => MapIssueToDto(i, repo.Name)));
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug(ex, "Skipping issues for repo {Owner}/{Repo}", owner, repo.Name);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch GitHub issues for owner {Owner}", owner);
        }

        return items;
    }

    private async Task<List<DevPortalPipelineDto>> FetchGitHubPipelines(
        IntegrationSettingsDto settings, string? repository)
    {
        var pipelines = new List<DevPortalPipelineDto>();
        var client = CreateGitHubClient(settings);
        if (client == null) return pipelines;

        var owner = settings.GitHub?.Organization ?? _gatewayOptions.GitHub?.DefaultOwner;
        if (string.IsNullOrEmpty(owner)) return pipelines;

        try
        {
            // If a specific repo is given, fetch runs for it; otherwise fetch from top repos
            var repoNames = new List<string>();
            if (!string.IsNullOrEmpty(repository))
            {
                repoNames.Add(repository);
            }
            else
            {
                IReadOnlyList<Octokit.Repository>? repos;
                try { repos = await client.Repository.GetAllForOrg(owner); }
                catch { repos = await client.Repository.GetAllForUser(owner); }
                if (repos != null)
                    repoNames.AddRange(repos.Take(10).Select(r => r.Name));
            }

            foreach (var repoName in repoNames)
            {
                try
                {
                    var runs = await client.Actions.Workflows.Runs.List(owner, repoName);
                    if (runs?.WorkflowRuns != null)
                    {
                        pipelines.AddRange(runs.WorkflowRuns.Take(10).Select(r => new DevPortalPipelineDto
                        {
                            Id = r.Id.ToString(),
                            Provider = "GitHub",
                            Name = r.Name,
                            RepositoryName = repoName,
                            Status = r.Status.StringValue,
                            Conclusion = r.Conclusion?.StringValue,
                            Branch = r.HeadBranch,
                            Url = r.HtmlUrl,
                            LastRunAt = r.CreatedAt.UtcDateTime,
                            TriggerEvent = r.Event
                        }));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Skipping pipelines for repo {Owner}/{Repo}", owner, repoName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch GitHub Actions for owner {Owner}", owner);
        }

        return pipelines;
    }

    private GitHubClient? CreateGitHubClient(IntegrationSettingsDto settings)
    {
        var token = settings.GitHub?.Token;
        if (string.IsNullOrEmpty(token))
            token = _gatewayOptions.GitHub?.AccessToken;
        if (string.IsNullOrEmpty(token))
            return null;

        var apiUrl = settings.GitHub?.ApiBaseUrl;
        if (string.IsNullOrEmpty(apiUrl) || apiUrl == "https://api.github.com")
            apiUrl = _gatewayOptions.GitHub?.ApiBaseUrl;

        GitHubClient client;
        if (!string.IsNullOrEmpty(apiUrl) && apiUrl != "https://api.github.com")
            client = new GitHubClient(new ProductHeaderValue("platform-engineering-copilot"), new Uri(apiUrl));
        else
            client = new GitHubClient(new ProductHeaderValue("platform-engineering-copilot"));

        client.Credentials = new Credentials(token);
        return client;
    }

    private static DevPortalWorkItemDto MapIssueToDto(Issue i, string repoName) => new()
    {
        Id = i.Id.ToString(),
        Provider = "GitHub",
        Type = i.PullRequest != null ? "PullRequest" : "Issue",
        Title = i.Title,
        Description = i.Body,
        State = i.State.StringValue,
        AssignedTo = i.Assignee?.Login,
        Labels = i.Labels?.Select(l => l.Name).ToList() ?? new(),
        RepositoryName = repoName,
        Url = i.HtmlUrl,
        Number = i.Number,
        CreatedAt = i.CreatedAt.UtcDateTime,
        UpdatedAt = i.UpdatedAt?.UtcDateTime,
        ClosedAt = i.ClosedAt?.UtcDateTime
    };

    #endregion

    #region ADO Helpers

    private async Task<List<DevPortalRepoDto>> FetchAdoRepos(IntegrationSettingsDto settings)
    {
        var repos = new List<DevPortalRepoDto>();
        var (httpClient, baseApiUrl) = CreateAdoHttpClient(settings);
        if (httpClient == null || baseApiUrl == null) return repos;

        using (httpClient)
        {
            try
            {
                var project = settings.AzureDevOps?.Project;
                var apiUrl = string.IsNullOrEmpty(project)
                    ? $"{baseApiUrl}/_apis/git/repositories?api-version=6.0"
                    : $"{baseApiUrl}/{project}/_apis/git/repositories?api-version=6.0";

                var response = await httpClient.GetAsync(apiUrl);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("ADO repos API returned {Status}", response.StatusCode);
                    return repos;
                }

                var body = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);

                if (doc.RootElement.TryGetProperty("value", out var repoArray))
                {
                    foreach (var r in repoArray.EnumerateArray())
                    {
                        var projName = r.TryGetProperty("project", out var proj)
                            ? (proj.TryGetProperty("name", out var pn) ? pn.GetString() : null)
                            : null;

                        repos.Add(new DevPortalRepoDto
                        {
                            Id = r.TryGetProperty("id", out var rid) ? rid.GetString() ?? "" : "",
                            Provider = settings.AzureDevOps?.ServerType == "server" ? "AdoServer" : "AzureDevOps",
                            Name = r.TryGetProperty("name", out var rn) ? rn.GetString() ?? "" : "",
                            FullName = $"{projName}/{(r.TryGetProperty("name", out var fn) ? fn.GetString() : "")}",
                            Description = projName ?? "",
                            DefaultBranch = r.TryGetProperty("defaultBranch", out var db)
                                ? (db.GetString()?.Replace("refs/heads/", "") ?? "main") : "main",
                            Url = r.TryGetProperty("webUrl", out var wu) ? wu.GetString() ?? "" : "",
                            CloneUrl = r.TryGetProperty("remoteUrl", out var ru) ? ru.GetString() ?? "" : "",
                            IsPrivate = true
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch ADO repositories");
            }
        }
        return repos;
    }

    private async Task<List<DevPortalWorkItemDto>> FetchAdoWorkItems(
        IntegrationSettingsDto settings, string? state)
    {
        var items = new List<DevPortalWorkItemDto>();
        var (httpClient, baseApiUrl) = CreateAdoHttpClient(settings);
        if (httpClient == null || baseApiUrl == null) return items;

        using (httpClient)
        {
            try
            {
                var project = settings.AzureDevOps?.Project;
                var wiqlUrl = string.IsNullOrEmpty(project)
                    ? $"{baseApiUrl}/_apis/wit/wiql?api-version=6.0"
                    : $"{baseApiUrl}/{project}/_apis/wit/wiql?api-version=6.0";

                // WIQL query for work items
                var stateClause = state switch
                {
                    "closed" => "AND [System.State] IN ('Closed', 'Done', 'Resolved', 'Completed')",
                    "open" => "AND [System.State] NOT IN ('Closed', 'Done', 'Resolved', 'Completed', 'Removed')",
                    _ => ""
                };

                var projectClause = string.IsNullOrEmpty(project) ? "" : $"AND [System.TeamProject] = '{project}'";

                var wiqlQuery = new
                {
                    query = $"SELECT [System.Id] FROM WorkItems WHERE [System.Id] > 0 {projectClause} {stateClause} ORDER BY [System.ChangedDate] DESC"
                };

                var wiqlResponse = await httpClient.PostAsJsonAsync(wiqlUrl, wiqlQuery);
                if (!wiqlResponse.IsSuccessStatusCode)
                {
                    _logger.LogWarning("ADO WIQL API returned {Status}", wiqlResponse.StatusCode);
                    return items;
                }

                var wiqlBody = await wiqlResponse.Content.ReadAsStringAsync();
                using var wiqlDoc = JsonDocument.Parse(wiqlBody);

                if (!wiqlDoc.RootElement.TryGetProperty("workItems", out var workItemRefs))
                    return items;

                // Get the first 50 work item IDs
                var ids = workItemRefs.EnumerateArray()
                    .Take(50)
                    .Select(wi => wi.GetProperty("id").GetInt32())
                    .ToList();

                if (ids.Count == 0) return items;

                // Batch fetch work item details
                var idsStr = string.Join(",", ids);
                var detailUrl = string.IsNullOrEmpty(project)
                    ? $"{baseApiUrl}/_apis/wit/workitems?ids={idsStr}&$expand=all&api-version=6.0"
                    : $"{baseApiUrl}/{project}/_apis/wit/workitems?ids={idsStr}&$expand=all&api-version=6.0";

                var detailResponse = await httpClient.GetAsync(detailUrl);
                if (!detailResponse.IsSuccessStatusCode) return items;

                var detailBody = await detailResponse.Content.ReadAsStringAsync();
                using var detailDoc = JsonDocument.Parse(detailBody);

                if (detailDoc.RootElement.TryGetProperty("value", out var wiArray))
                {
                    var providerName = settings.AzureDevOps?.ServerType == "server" ? "AdoServer" : "AzureDevOps";

                    foreach (var wi in wiArray.EnumerateArray())
                    {
                        var fields = wi.GetProperty("fields");
                        var wiId = wi.GetProperty("id").GetInt32();

                        items.Add(new DevPortalWorkItemDto
                        {
                            Id = wiId.ToString(),
                            Provider = providerName,
                            Type = GetJsonString(fields, "System.WorkItemType") ?? "Task",
                            Title = GetJsonString(fields, "System.Title") ?? "",
                            Description = GetJsonString(fields, "System.Description"),
                            State = GetJsonString(fields, "System.State") ?? "Unknown",
                            AssignedTo = fields.TryGetProperty("System.AssignedTo", out var at)
                                ? (at.TryGetProperty("displayName", out var dn) ? dn.GetString() : at.GetString())
                                : null,
                            RepositoryName = GetJsonString(fields, "System.TeamProject") ?? "",
                            Url = wi.TryGetProperty("_links", out var links)
                                ? (links.TryGetProperty("html", out var html)
                                    ? (html.TryGetProperty("href", out var href) ? href.GetString() ?? "" : "") : "") : "",
                            Number = wiId,
                            CreatedAt = fields.TryGetProperty("System.CreatedDate", out var cd)
                                ? cd.GetDateTime() : DateTime.UtcNow,
                            UpdatedAt = fields.TryGetProperty("System.ChangedDate", out var ud)
                                ? ud.GetDateTime() : null
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch ADO work items");
            }
        }
        return items;
    }

    private async Task<List<DevPortalPipelineDto>> FetchAdoPipelines(IntegrationSettingsDto settings)
    {
        var pipelines = new List<DevPortalPipelineDto>();
        var (httpClient, baseApiUrl) = CreateAdoHttpClient(settings);
        if (httpClient == null || baseApiUrl == null) return pipelines;

        using (httpClient)
        {
            try
            {
                var project = settings.AzureDevOps?.Project;
                var providerName = settings.AzureDevOps?.ServerType == "server" ? "AdoServer" : "AzureDevOps";

                // If no project specified, discover projects first (required for ADO Server build APIs)
                var projects = new List<string>();
                if (string.IsNullOrEmpty(project))
                {
                    var projUrl = $"{baseApiUrl}/_apis/projects?api-version=6.0";
                    _logger.LogInformation("Fetching ADO projects from: {Url}", projUrl);
                    var projResponse = await httpClient.GetAsync(projUrl);
                    if (projResponse.IsSuccessStatusCode)
                    {
                        var projBody = await projResponse.Content.ReadAsStringAsync();
                        using var projDoc = JsonDocument.Parse(projBody);
                        if (projDoc.RootElement.TryGetProperty("value", out var projArray))
                        {
                            foreach (var p in projArray.EnumerateArray())
                            {
                                var pName = p.TryGetProperty("name", out var pn) ? pn.GetString() : null;
                                if (!string.IsNullOrEmpty(pName))
                                    projects.Add(pName);
                            }
                        }
                        _logger.LogInformation("Discovered {Count} ADO projects", projects.Count);
                    }
                    else
                    {
                        _logger.LogWarning("ADO projects API returned {Status}", projResponse.StatusCode);
                    }
                }
                else
                {
                    projects.Add(project);
                }

                // Fetch pipeline definitions per project
                foreach (var proj in projects)
                {
                    await FetchProjectPipelines(httpClient, baseApiUrl, proj, providerName, pipelines);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch ADO builds/pipelines");
            }
        }
        return pipelines;
    }

    private async Task FetchProjectPipelines(HttpClient httpClient, string baseApiUrl, string project,
        string providerName, List<DevPortalPipelineDto> pipelines)
    {
        // Try build definitions first (includes latest build info)
        var defUrl = $"{baseApiUrl}/{Uri.EscapeDataString(project)}/_apis/build/definitions?includeLatestBuilds=true&api-version=6.0";
        _logger.LogInformation("Fetching ADO pipeline definitions from: {Url}", defUrl);
        var defResponse = await httpClient.GetAsync(defUrl);

        if (defResponse.IsSuccessStatusCode)
        {
            var defBody = await defResponse.Content.ReadAsStringAsync();
            using var defDoc = JsonDocument.Parse(defBody);

            if (defDoc.RootElement.TryGetProperty("value", out var defArray))
            {
                foreach (var def in defArray.EnumerateArray())
                {
                    var latestBuild = def.TryGetProperty("latestBuild", out var lb) ? lb : default;
                    var latestCompletedBuild = def.TryGetProperty("latestCompletedBuild", out var lcb) ? lcb : default;
                    var activeBuild = latestCompletedBuild.ValueKind != JsonValueKind.Undefined ? latestCompletedBuild : latestBuild;
                    var repoInfo = def.TryGetProperty("repository", out var ri) ? ri : default;

                    pipelines.Add(new DevPortalPipelineDto
                    {
                        Id = def.TryGetProperty("id", out var did) ? did.GetInt32().ToString() : "",
                        Provider = providerName,
                        Name = def.TryGetProperty("name", out var dn) ? dn.GetString() ?? "" : "",
                        RepositoryName = repoInfo.ValueKind != JsonValueKind.Undefined && repoInfo.TryGetProperty("name", out var rn)
                            ? rn.GetString() ?? "" : "",
                        Status = activeBuild.ValueKind != JsonValueKind.Undefined
                            ? GetJsonString(activeBuild, "status") : "notStarted",
                        Conclusion = activeBuild.ValueKind != JsonValueKind.Undefined
                            ? GetJsonString(activeBuild, "result") : null,
                        Branch = activeBuild.ValueKind != JsonValueKind.Undefined
                            ? GetJsonString(activeBuild, "sourceBranch")?.Replace("refs/heads/", "")
                            : null,
                        Url = def.TryGetProperty("_links", out var links)
                            ? (links.TryGetProperty("web", out var web)
                                ? (web.TryGetProperty("href", out var href) ? href.GetString() ?? "" : "") : "") : "",
                        LastRunAt = activeBuild.ValueKind != JsonValueKind.Undefined
                            ? (activeBuild.TryGetProperty("finishTime", out var ft) && ft.ValueKind == JsonValueKind.String
                                ? ft.GetDateTime()
                                : (activeBuild.TryGetProperty("startTime", out var st) && st.ValueKind == JsonValueKind.String ? st.GetDateTime() : null))
                            : null,
                        TriggerEvent = activeBuild.ValueKind != JsonValueKind.Undefined
                            ? GetJsonString(activeBuild, "reason") : null
                    });
                }
                _logger.LogInformation("Found {Count} pipeline definitions in project {Project}", pipelines.Count, project);
                return;
            }
        }
        else
        {
            var errBody = await defResponse.Content.ReadAsStringAsync();
            _logger.LogWarning("ADO definitions API returned {Status} for project {Project}: {Body}",
                defResponse.StatusCode, project, errBody.Length > 200 ? errBody[..200] : errBody);
        }

        // Fallback: fetch recent builds
        var buildsUrl = $"{baseApiUrl}/{Uri.EscapeDataString(project)}/_apis/build/builds?$top=50&api-version=6.0";
        _logger.LogInformation("Falling back to builds API for project {Project}: {Url}", project, buildsUrl);
        var response = await httpClient.GetAsync(buildsUrl);

        if (response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);

            if (doc.RootElement.TryGetProperty("value", out var buildArray))
            {
                foreach (var b in buildArray.EnumerateArray())
                {
                    var bDef = b.TryGetProperty("definition", out var d) ? d : default;
                    var bRepo = b.TryGetProperty("repository", out var bri) ? bri : default;

                    pipelines.Add(new DevPortalPipelineDto
                    {
                        Id = b.TryGetProperty("id", out var bid) ? bid.GetInt32().ToString() : "",
                        Provider = providerName,
                        Name = bDef.ValueKind != JsonValueKind.Undefined && bDef.TryGetProperty("name", out var dn2)
                            ? dn2.GetString() ?? "" : "",
                        RepositoryName = bRepo.ValueKind != JsonValueKind.Undefined && bRepo.TryGetProperty("name", out var rn2)
                            ? rn2.GetString() ?? "" : "",
                        Status = GetJsonString(b, "status"),
                        Conclusion = GetJsonString(b, "result"),
                        Branch = GetJsonString(b, "sourceBranch")?.Replace("refs/heads/", ""),
                        Url = b.TryGetProperty("_links", out var links2)
                            ? (links2.TryGetProperty("web", out var web2)
                                ? (web2.TryGetProperty("href", out var href2) ? href2.GetString() ?? "" : "") : "") : "",
                        LastRunAt = b.TryGetProperty("finishTime", out var ft2) && ft2.ValueKind == JsonValueKind.String
                            ? ft2.GetDateTime()
                            : (b.TryGetProperty("startTime", out var st2) && st2.ValueKind == JsonValueKind.String ? st2.GetDateTime() : null),
                        TriggerEvent = GetJsonString(b, "reason")
                    });
                }
                _logger.LogInformation("Found {Count} builds in project {Project}", pipelines.Count, project);
            }
        }
        else
        {
            var errBody = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("ADO builds API returned {Status} for project {Project}: {Body}",
                response.StatusCode, project, errBody.Length > 200 ? errBody[..200] : errBody);
        }
    }

    /// <summary>
    /// Creates an HttpClient configured for ADO REST API with PAT auth.
    /// Returns null if no ADO integration is configured.
    /// </summary>
    private (HttpClient? Client, string? BaseApiUrl) CreateAdoHttpClient(IntegrationSettingsDto settings)
    {
        var adoSettings = settings.AzureDevOps;
        var token = adoSettings?.AccessToken;
        if (string.IsNullOrEmpty(token))
            token = _gatewayOptions.AzureDevOps?.AccessToken;
        if (string.IsNullOrEmpty(token)) return (null, null);

        var serverUrl = adoSettings?.ServerUrl;
        if (string.IsNullOrEmpty(serverUrl))
            serverUrl = _gatewayOptions.AzureDevOps?.ServerUrl;
        if (string.IsNullOrEmpty(serverUrl)) return (null, null);

        var baseUrl = serverUrl.TrimEnd('/');
        string baseApiUrl;

        if (adoSettings?.ServerType == "server")
        {
            var collection = adoSettings.DefaultCollection ?? "DefaultCollection";
            baseApiUrl = $"{baseUrl}/{collection}";
        }
        else
        {
            // ADO Services (cloud) - URL already includes organization
            baseApiUrl = baseUrl;
        }

        var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var encodedPat = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($":{token}"));
        httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", encodedPat);

        return (httpClient, baseApiUrl);
    }

    private static string? GetJsonString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var val) && val.ValueKind == JsonValueKind.String
            ? val.GetString()
            : null;
    }

    #endregion

    #region Integration Settings

    private IntegrationSettingsDto LoadIntegrationSettings()
    {
        try
        {
            return IntegrationsController.LoadSettings();
        }
        catch
        {
            return new IntegrationSettingsDto();
        }
    }

    #endregion
}

#region DTOs

public class IntegrationStatusDto
{
    public string Provider { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string? Organization { get; set; }
    public string? ServerUrl { get; set; }
}

public class DevPortalSummaryDto
{
    public int EnabledIntegrations { get; set; }
    public int TotalRepositories { get; set; }
    public int OpenWorkItems { get; set; }
    public int ActivePipelines { get; set; }
    public int RecentDeployments { get; set; }
    public List<IntegrationStatusDto> Integrations { get; set; } = new();
}

public class DevPortalRepoDto
{
    public string Id { get; set; } = string.Empty;
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

#endregion
