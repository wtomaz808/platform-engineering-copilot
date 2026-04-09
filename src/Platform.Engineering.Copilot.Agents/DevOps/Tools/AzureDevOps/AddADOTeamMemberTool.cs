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
/// Tool for adding a user to an Azure DevOps team.
/// Resolves the user identity first then adds them to the team via the Teams Members API.
/// Parallel to AddGitHubTeamMemberTool.
/// </summary>
public class AddADOTeamMemberTool : BaseTool
{
    public override string Name => "add_ado_team_member";

    public override string Description =>
        "Add a user to an Azure DevOps team. " +
        "Resolves the user by email or display name then adds them to the specified team. " +
        "Use list_ado_teams first to find the team name. Requires that the user already has access to the ADO organization.";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GatewayOptions _gatewayOptions;
    private readonly DevOpsAgentOptions _devOpsOptions;

    public AddADOTeamMemberTool(
        ILogger<AddADOTeamMemberTool> logger,
        IHttpClientFactory httpClientFactory,
        IOptions<GatewayOptions> gatewayOptions,
        IOptions<DevOpsAgentOptions> devOpsOptions)
        : base(logger)
    {
        _httpClientFactory = httpClientFactory;
        _gatewayOptions = gatewayOptions?.Value ?? new GatewayOptions();
        _devOpsOptions = devOpsOptions?.Value ?? new DevOpsAgentOptions();

        Parameters.Add(new ToolParameter("project", "Project name", true));
        Parameters.Add(new ToolParameter("team", "Team name to add the user to", true));
        Parameters.Add(new ToolParameter("user", "User email address or display name to add", true));
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
            var teamName = GetArg(arguments, "team") ?? "";
            var userIdentifier = GetArg(arguments, "user") ?? "";

            if (string.IsNullOrWhiteSpace(project)) return JsonSerializer.Serialize(new { success = false, error = "project is required." });
            if (string.IsNullOrWhiteSpace(teamName)) return JsonSerializer.Serialize(new { success = false, error = "team is required." });
            if (string.IsNullOrWhiteSpace(userIdentifier)) return JsonSerializer.Serialize(new { success = false, error = "user is required." });

            var baseUrl = BuildBaseUrl(adoGw);
            using var client = CreateAuthenticatedClient(adoGw.AccessToken);

            // Step 1: Resolve team ID
            var teamsUrl = $"{baseUrl}/_apis/projects/{Uri.EscapeDataString(project)}/teams?api-version=6.0";
            var teamsResp = await client.GetAsync(teamsUrl, cancellationToken);
            if (!teamsResp.IsSuccessStatusCode)
                return JsonSerializer.Serialize(new { success = false, error = "Could not retrieve teams list. Check project name." });

            var teamsJson = JsonSerializer.Deserialize<JsonElement>(await teamsResp.Content.ReadAsStringAsync(cancellationToken));
            var teamId = teamsJson.TryGetProperty("value", out var tv)
                ? tv.EnumerateArray()
                    .FirstOrDefault(t => t.TryGetProperty("name", out var tn) &&
                        string.Equals(tn.GetString(), teamName, StringComparison.OrdinalIgnoreCase))
                    .TryGetProperty("id", out var tid) ? tid.GetString() : null
                : null;

            if (string.IsNullOrWhiteSpace(teamId))
                return JsonSerializer.Serialize(new { success = false, error = $"Team '{teamName}' not found in project '{project}'. Use list_ado_teams to see available teams." });

            // Step 2: Resolve user identity via IdentityPicker
            var identityUrl = $"{baseUrl}/_apis/IdentityPicker/Identities?api-version=6.0-preview.1";
            var identityBody = JsonSerializer.Serialize(new
            {
                query = userIdentifier,
                identityTypes = new[] { "user" },
                operationScopes = new[] { "ims", "source" },
                properties = new[] { "DisplayName", "SubjectDescriptor", "Mail" }
            });

            var identityContent = new StringContent(identityBody, Encoding.UTF8, "application/json");
            var identityResp = await client.PostAsync(identityUrl, identityContent, cancellationToken);

            string? userId = null;
            if (identityResp.IsSuccessStatusCode)
            {
                var identityJson = JsonSerializer.Deserialize<JsonElement>(await identityResp.Content.ReadAsStringAsync(cancellationToken));
                if (identityJson.TryGetProperty("results", out var results) &&
                    results.GetArrayLength() > 0 &&
                    results[0].TryGetProperty("identities", out var identities) &&
                    identities.GetArrayLength() > 0)
                {
                    var identity = identities[0];
                    userId = identity.TryGetProperty("localId", out var lid) ? lid.GetString() : null;
                }
            }

            if (string.IsNullOrWhiteSpace(userId))
            {
                // Try a simpler approach via the members endpoint directly with the email
                // This works on ADO Server when the user is already in the directory
                var addDirectUrl = $"{baseUrl}/_apis/projects/{Uri.EscapeDataString(project)}/teams/{teamId}/members?api-version=6.0";
                var addContent = new StringContent(JsonSerializer.Serialize(new { id = userIdentifier }), Encoding.UTF8, "application/json");
                var addResp = await client.PutAsync(addDirectUrl, addContent, cancellationToken);
                if (addResp.IsSuccessStatusCode)
                    return JsonSerializer.Serialize(new { success = true, project, team = teamName, user = userIdentifier, message = $"User '{userIdentifier}' added to team '{teamName}'." });

                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = $"Could not resolve identity for '{userIdentifier}'. Ensure the user has an ADO account and is part of the organization.",
                    hint = "The user must already have an ADO account. Try their email address."
                });
            }

            // Step 3: Add to team
            var memberUrl = $"{baseUrl}/_apis/projects/{Uri.EscapeDataString(project)}/teams/{teamId}/members?api-version=6.0";
            var memberContent = new StringContent(JsonSerializer.Serialize(new { id = userId }), Encoding.UTF8, "application/json");
            var memberResp = await client.PutAsync(memberUrl, memberContent, cancellationToken);

            if (!memberResp.IsSuccessStatusCode)
            {
                var errBody = await memberResp.Content.ReadAsStringAsync(cancellationToken);
                return JsonSerializer.Serialize(new { success = false, error = $"Failed to add member: {Truncate(errBody, 300)}" });
            }

            return JsonSerializer.Serialize(new
            {
                success = true,
                project,
                team = teamName,
                teamId,
                user = userIdentifier,
                userId,
                message = $"User '{userIdentifier}' successfully added to team '{teamName}'."
            });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error adding ADO team member");
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
