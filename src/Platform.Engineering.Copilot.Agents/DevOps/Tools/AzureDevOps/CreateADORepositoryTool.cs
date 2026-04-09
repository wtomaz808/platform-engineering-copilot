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
/// Tool for creating a new Git repository in an Azure DevOps project.
/// </summary>
public class CreateADORepositoryTool : BaseTool
{
    public override string Name => "create_ado_repository";

    public override string Description =>
        "Create a new Git repository in an Azure DevOps project. " +
        "Optionally initialize with a README and set the default branch. " +
        "Use this to set up new source code repositories under a project.";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GatewayOptions _gatewayOptions;
    private readonly DevOpsAgentOptions _devOpsOptions;

    public CreateADORepositoryTool(
        ILogger<CreateADORepositoryTool> logger,
        IHttpClientFactory httpClientFactory,
        IOptions<GatewayOptions> gatewayOptions,
        IOptions<DevOpsAgentOptions> devOpsOptions)
        : base(logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _gatewayOptions = gatewayOptions?.Value ?? throw new ArgumentNullException(nameof(gatewayOptions));
        _devOpsOptions = devOpsOptions?.Value ?? throw new ArgumentNullException(nameof(devOpsOptions));

        Parameters.Add(new ToolParameter("project", "Project name to create the repository in", true));
        Parameters.Add(new ToolParameter("name", "Repository name", true));
        Parameters.Add(new ToolParameter("default_branch", "Default branch name (default: main)", false));
    }

    public override async Task<string> ExecuteAsync(
        IDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var adoGw = _gatewayOptions.AzureDevOps;

            if (!adoGw.Enabled || string.IsNullOrWhiteSpace(adoGw.AccessToken) || string.IsNullOrWhiteSpace(adoGw.ServerUrl))
            {
                return JsonSerializer.Serialize(new { success = false, error = "Azure DevOps is not configured." });
            }

            var project = GetArg(arguments, "project") ?? _devOpsOptions.AzureDevOps.DefaultProject;
            var repoName = GetArg(arguments, "name");
            var defaultBranch = GetArg(arguments, "default_branch") ?? "main";

            if (string.IsNullOrWhiteSpace(project)) return JsonSerializer.Serialize(new { success = false, error = "Project name is required." });
            if (string.IsNullOrWhiteSpace(repoName)) return JsonSerializer.Serialize(new { success = false, error = "Repository name is required." });

            // Need to resolve project ID first
            var baseUrl = BuildBaseUrl(adoGw);
            using var client = CreateAuthenticatedClient(adoGw.AccessToken);

            var projectsUrl = $"{baseUrl}/_apis/projects/{Uri.EscapeDataString(project)}?api-version=6.0";
            var projectResp = await client.GetAsync(projectsUrl, cancellationToken);
            if (!projectResp.IsSuccessStatusCode)
            {
                var err = await projectResp.Content.ReadAsStringAsync(cancellationToken);
                return JsonSerializer.Serialize(new { success = false, error = $"Could not find project '{project}': {Truncate(err, 300)}" });
            }
            var projectJson = JsonSerializer.Deserialize<JsonElement>(await projectResp.Content.ReadAsStringAsync(cancellationToken));
            var projectId = projectJson.TryGetProperty("id", out var pid) ? pid.GetString() : null;

            if (string.IsNullOrWhiteSpace(projectId))
                return JsonSerializer.Serialize(new { success = false, error = $"Could not resolve project ID for '{project}'." });

            var createBody = JsonSerializer.Serialize(new
            {
                name = repoName,
                project = new { id = projectId },
                defaultBranch = $"refs/heads/{defaultBranch.TrimStart('/')}"
            });

            var createUrl = $"{baseUrl}/{Uri.EscapeDataString(project)}/_apis/git/repositories?api-version=6.0";
            Logger.LogInformation("Creating ADO repository {Name} in project {Project}", repoName, project);

            var content = new StringContent(createBody, Encoding.UTF8, "application/json");
            var resp = await client.PostAsync(createUrl, content, cancellationToken);
            var body = await resp.Content.ReadAsStringAsync(cancellationToken);

            if (!resp.IsSuccessStatusCode)
                return JsonSerializer.Serialize(new { success = false, error = $"ADO API returned {(int)resp.StatusCode}: {Truncate(body, 400)}" });

            var result = JsonSerializer.Deserialize<JsonElement>(body);
            return JsonSerializer.Serialize(new
            {
                success = true,
                name = repoName,
                project,
                id = result.TryGetProperty("id", out var repoId) ? repoId.GetString() : null,
                cloneUrl = result.TryGetProperty("remoteUrl", out var remote) ? remote.GetString() : null,
                sshUrl = result.TryGetProperty("sshUrl", out var ssh) ? ssh.GetString() : null,
                webUrl = result.TryGetProperty("webUrl", out var web) ? web.GetString() : null,
                defaultBranch,
                message = $"Repository '{repoName}' created successfully in project '{project}'."
            });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error creating ADO repository");
            return JsonSerializer.Serialize(new { success = false, error = ex.Message });
        }
    }

    private static string? GetArg(IDictionary<string, object?> args, string key) =>
        args.TryGetValue(key, out var v) ? v?.ToString() : null;

    private string BuildBaseUrl(AzureDevOpsGatewayOptions adoGw)
    {
        var url = adoGw.ServerUrl!.TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(adoGw.DefaultCollection))
            url = $"{url}/{adoGw.DefaultCollection.Trim('/')}";
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
