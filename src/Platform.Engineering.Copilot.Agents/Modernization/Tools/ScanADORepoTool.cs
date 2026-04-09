using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.Engineering.Copilot.Agents.Common;
using Platform.Engineering.Copilot.Agents.Modernization.Services;
using Platform.Engineering.Copilot.Core.Configuration;

namespace Platform.Engineering.Copilot.Agents.Modernization.Tools;

/// <summary>
/// Scans an Azure DevOps on-premises Git repository for .NET migration signals.
/// Fetches .csproj, web.config, packages.config, Dockerfile, and solution files
/// directly from the ADO Git Items API. Produces a migration readiness summary
/// ready for other modernization tools. Parallel to ScanGitHubRepoTool.
/// </summary>
public class ScanADORepoTool : BaseTool
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly CodeAnalysisService _codeAnalysis;
    private readonly GatewayOptions _gateway;

    public override string Name => "scan_ado_repo";

    public override string Description =>
        "Scan an Azure DevOps on-premises Git repository for .NET migration readiness without cloning it. " +
        "Fetches .csproj, web.config, packages.config, solution files, and Dockerfiles via the ADO Git Items API. " +
        "Returns a migration readiness summary including framework version, key dependencies, Windows-specific blockers, " +
        "and recommended Azure targets (App Service, AKS, Container Apps, Azure Functions). " +
        "Requires the ADO server to be configured in settings.";

    public ScanADORepoTool(
        ILogger<ScanADORepoTool> logger,
        IHttpClientFactory httpClientFactory,
        CodeAnalysisService codeAnalysis,
        IOptions<GatewayOptions> gateway) : base(logger)
    {
        _httpClientFactory = httpClientFactory;
        _codeAnalysis = codeAnalysis;
        _gateway = gateway?.Value ?? new GatewayOptions();

        Parameters.Add(new ToolParameter("project", "ADO project name", true));
        Parameters.Add(new ToolParameter("repository", "Repository name within the project", true));
        Parameters.Add(new ToolParameter("branch", "Branch to scan (default: main)", false));
        Parameters.Add(new ToolParameter("path", "Sub-directory path within the repository to scope the scan (default: root /)", false));
    }

    public override async Task<string> ExecuteAsync(
        IDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var adoGw = _gateway.AzureDevOps;
            if (!adoGw.Enabled || string.IsNullOrWhiteSpace(adoGw.AccessToken) || string.IsNullOrWhiteSpace(adoGw.ServerUrl))
                return JsonSerializer.Serialize(new { success = false, error = "Azure DevOps is not configured. Set the ADO server URL and PAT token in Settings." });

            var project = GetArg(arguments, "project") ?? "";
            var repoName = GetArg(arguments, "repository") ?? "";
            var branch = GetArg(arguments, "branch") ?? "main";
            var scanPath = (GetArg(arguments, "path") ?? "").TrimStart('/');

            if (string.IsNullOrWhiteSpace(project)) return JsonSerializer.Serialize(new { success = false, error = "project is required." });
            if (string.IsNullOrWhiteSpace(repoName)) return JsonSerializer.Serialize(new { success = false, error = "repository is required." });

            Logger.LogInformation("Scanning ADO repo {Project}/{Repo} branch {Branch}", project, repoName, branch);

            var baseUrl = BuildBaseUrl(adoGw);
            using var client = CreateAuthenticatedClient(adoGw.AccessToken);

            // Step 1: Get the full recursive file list
            var files = await ListRepoFilesAsync(client, baseUrl, project, repoName, branch, scanPath, cancellationToken);
            if (files == null)
                return JsonSerializer.Serialize(new { success = false, error = $"Could not list files in '{project}/{repoName}'. Verify the project, repository name, and branch." });

            // Step 2: Filter for significant files
            var targets = files
                .Where(IsSignificantFile)
                .Take(20)
                .ToList();

            Logger.LogInformation("Found {Count} significant files in {Project}/{Repo}", targets.Count, project, repoName);

            if (!targets.Any())
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    project,
                    repository = repoName,
                    branch,
                    warning = "No .NET project files (.csproj, web.config, packages.config, .sln) found. " +
                              "The repository may not be a .NET application, or the files are in a sub-directory — try specifying 'path'.",
                    allFilesFound = files.Take(50).ToList()
                });

            // Step 3: Fetch and analyse each file
            var projectAnalyses = new List<object>();
            var webConfigAnalyses = new List<object>();
            var dockerfiles = new List<string>();
            var solutionFiles = new List<string>();
            var rawFindings = new List<string>();

            foreach (var filePath in targets)
            {
                var content = await FetchFileContentAsync(client, baseUrl, project, repoName, filePath, branch, cancellationToken);
                if (string.IsNullOrWhiteSpace(content)) continue;

                var lower = filePath.ToLowerInvariant();

                if (lower.EndsWith(".csproj") || lower.EndsWith(".vbproj") || lower.EndsWith(".fsproj"))
                {
                    var parsed = _codeAnalysis.ParseCsprojContent(content);
                    projectAnalyses.Add(new
                    {
                        file = filePath,
                        targetFramework = parsed.TargetFramework,
                        isSdkStyle = parsed.IsSdkStyle,
                        outputType = parsed.OutputType,
                        packageCount = parsed.PackageReferences.Count,
                        comReferenceCount = parsed.ComReferences.Count,
                        assemblyReferenceCount = parsed.AssemblyReferences.Count,
                        packages = parsed.PackageReferences.Select(p => $"{p.Name} {p.Version}").Take(30).ToList(),
                        comReferences = parsed.ComReferences,
                        projectReferences = parsed.ProjectReferences.Take(10).ToList()
                    });
                }
                else if (lower.EndsWith("web.config") || lower.EndsWith("app.config"))
                {
                    var parsed = _codeAnalysis.ParseWebConfigContent(content);
                    webConfigAnalyses.Add(new
                    {
                        file = filePath,
                        hasWcf = parsed.HasWcf,
                        hasMsmq = parsed.HasMsmq,
                        authMode = parsed.AuthenticationMode,
                        connectionStrings = parsed.ConnectionStrings.Select(c => $"{c.Name} ({c.DetectedDbType})").ToList(),
                        wcfServices = parsed.WcfServices,
                        iisModuleCount = parsed.IisModules.Count
                    });
                }
                else if (lower.EndsWith("dockerfile"))
                {
                    dockerfiles.Add(filePath);
                    rawFindings.Add($"Dockerfile found at {filePath} — app may already be containerized");
                }
                else if (lower.EndsWith(".sln"))
                {
                    solutionFiles.Add(filePath);
                }
                else if (lower.EndsWith("packages.config"))
                {
                    var packageCount = content.Split("<package ").Length - 1;
                    rawFindings.Add($"packages.config at {filePath} — {packageCount} NuGet packages (pre-SDK style)");
                }
            }

            // Step 4: Synthesize summary
            var summary = BuildSummary(project, repoName, branch, projectAnalyses, webConfigAnalyses, dockerfiles, solutionFiles, rawFindings);

            return JsonSerializer.Serialize(new
            {
                success = true,
                project,
                repository = repoName,
                branch,
                scanPath = string.IsNullOrEmpty(scanPath) ? "/" : "/" + scanPath,
                filesScanned = targets.Count,
                projectFiles = projectAnalyses,
                configFiles = webConfigAnalyses,
                dockerfiles,
                solutionFiles,
                rawFindings,
                migrationSummary = summary,
                nextSteps = new[]
                {
                    "Run 'app_assessment' tool with the project findings above for a detailed readiness score",
                    "Run 'azure_target_recommendation' tool with the app profile for Azure service mapping",
                    "Run 'containerization_assessment' tool if containers are the target",
                    "Run 'migration_plan_generator' tool to produce a phased migration plan"
                }
            }, new JsonSerializerOptions { WriteIndented = false });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error scanning ADO repo");
            return JsonSerializer.Serialize(new { success = false, error = ex.Message });
        }
    }

    private async Task<List<string>?> ListRepoFilesAsync(
        HttpClient client, string baseUrl, string project, string repo, string branch, string path, CancellationToken ct)
    {
        try
        {
            // Use the items API with Full recursion
            var url = $"{baseUrl}/{Uri.EscapeDataString(project)}/_apis/git/repositories/{Uri.EscapeDataString(repo)}/items" +
                      $"?recursionLevel=Full&versionDescriptor.version={Uri.EscapeDataString(branch)}" +
                      $"&versionDescriptor.versionType=branch&api-version=6.0";

            var resp = await client.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return null;

            var json = JsonSerializer.Deserialize<JsonElement>(await resp.Content.ReadAsStringAsync(ct));
            var items = json.TryGetProperty("value", out var v) ? v : default;
            if (items.ValueKind != JsonValueKind.Array) return null;

            return items.EnumerateArray()
                .Where(i => i.TryGetProperty("gitObjectType", out var t) && t.GetString() == "blob")
                .Select(i => i.TryGetProperty("path", out var p) ? p.GetString()?.TrimStart('/') ?? "" : "")
                .Where(p => string.IsNullOrEmpty(path) || p.StartsWith(path, StringComparison.OrdinalIgnoreCase))
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList();
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> FetchFileContentAsync(
        HttpClient client, string baseUrl, string project, string repo, string filePath, string branch, CancellationToken ct)
    {
        try
        {
            var encodedPath = Uri.EscapeDataString("/" + filePath.TrimStart('/'));
            var url = $"{baseUrl}/{Uri.EscapeDataString(project)}/_apis/git/repositories/{Uri.EscapeDataString(repo)}/items" +
                      $"?path={encodedPath}&versionDescriptor.version={Uri.EscapeDataString(branch)}" +
                      $"&versionDescriptor.versionType=branch&api-version=6.0";

            // Request plain text content
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.Clear();
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));

            var resp = await client.SendAsync(request, ct);
            if (!resp.IsSuccessStatusCode) return null;

            var content = await resp.Content.ReadAsStringAsync(ct);

            // ADO sometimes returns JSON wrapper even with text/plain — unwrap if needed
            if (content.TrimStart().StartsWith("{"))
            {
                try
                {
                    var json = JsonSerializer.Deserialize<JsonElement>(content);
                    if (json.TryGetProperty("content", out var c)) return c.GetString();
                }
                catch { }
            }

            return content;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsSignificantFile(string path)
    {
        var lower = path.ToLowerInvariant();
        return lower.EndsWith(".csproj") || lower.EndsWith(".vbproj") || lower.EndsWith(".fsproj") ||
               lower.EndsWith("web.config") || lower.EndsWith("app.config") ||
               lower.EndsWith("packages.config") || lower.EndsWith(".sln") ||
               lower == "dockerfile" || lower.EndsWith("/dockerfile");
    }

    private static object BuildSummary(
        string project, string repo, string branch,
        List<object> projects, List<object> webConfigs,
        List<string> dockerfiles, List<string> solutions, List<string> rawFindings)
    {
        var frameworks = projects
            .Select(p => (p as dynamic)?.targetFramework as string)
            .Where(f => !string.IsNullOrEmpty(f))
            .Distinct().ToList();

        var hasComRefs = projects.Any(p => ((p as dynamic)?.comReferenceCount as int? ?? 0) > 0);
        var hasWcf = webConfigs.Any(w => (w as dynamic)?.hasWcf as bool? == true);
        var hasMsmq = webConfigs.Any(w => (w as dynamic)?.hasMsmq as bool? == true);
        var isContainerized = dockerfiles.Any();
        var totalPkgs = projects.Sum(p => (p as dynamic)?.packageCount as int? ?? 0);

        var complexity = "Low";
        var blockers = new List<string>();

        if (hasComRefs) { blockers.Add("COM References — requires Windows VM or refactoring"); complexity = "High"; }
        if (hasWcf) { blockers.Add("WCF bindings — consider CoreWCF or gRPC"); if (complexity != "High") complexity = "Medium"; }
        if (hasMsmq) { blockers.Add("MSMQ — migrate to Azure Service Bus"); if (complexity != "High") complexity = "Medium"; }

        var legacyFw = frameworks.Where(f => f != null && (f.Contains("net3") || f.Contains("net4") || f.Contains("v3") || f.Contains("v4"))).ToList();
        if (legacyFw.Any())
        {
            blockers.Add($"Legacy .NET Framework ({string.Join(", ", legacyFw)}) — upgrade to .NET 8/9");
            if (complexity != "High") complexity = "Medium";
        }

        var targets = new List<string>();
        if (isContainerized) targets.Add("Azure Kubernetes Service (already has Dockerfile)");
        else if (!hasComRefs && !hasWcf) targets.AddRange(new[] { "Azure App Service", "Azure Container Apps" });
        if (hasWcf) targets.Add("Azure App Service (CoreWCF)");
        if (hasComRefs) targets.Add("Azure VM (lift-and-shift required first)");
        if (!targets.Any()) targets.Add("Azure App Service");

        return new
        {
            adoProject = project,
            repository = repo,
            branch,
            projectCount = projects.Count,
            solutionCount = solutions.Count,
            frameworks,
            totalNuGetPackages = totalPkgs,
            migrationComplexity = complexity,
            blockers,
            isAlreadyContainerized = isContainerized,
            recommendedAzureTargets = targets,
            additionalFindings = rawFindings
        };
    }

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
        client.Timeout = TimeSpan.FromSeconds(30);
        var encoded = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{accessToken}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", encoded);
        return client;
    }

    private static string? GetArg(IDictionary<string, object?> args, string key) =>
        args.TryGetValue(key, out var v) ? v?.ToString() : null;
}
