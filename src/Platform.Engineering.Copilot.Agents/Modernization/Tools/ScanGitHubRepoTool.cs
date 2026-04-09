using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Platform.Engineering.Copilot.Agents.Common;
using Platform.Engineering.Copilot.Agents.Modernization.Services;
using Platform.Engineering.Copilot.Core.Configuration;
using Microsoft.Extensions.Options;

namespace Platform.Engineering.Copilot.Agents.Modernization.Tools;

/// <summary>
/// Scans a GitHub repository (public or private) for .NET migration signals.
/// Fetches .csproj, web.config, packages.config, Dockerfile, and solution files
/// directly from the GitHub API, then runs them through code analysis to produce
/// a migration readiness summary ready for other modernization tools.
/// </summary>
public class ScanGitHubRepoTool : BaseTool
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly CodeAnalysisService _codeAnalysis;
    private readonly GatewayOptions _gateway;

    public override string Name => "scan_github_repo";

    public override string Description =>
        "Scan a GitHub repository for .NET migration readiness without cloning it. " +
        "Fetches .csproj, web.config, packages.config, solution files, and Dockerfiles via the GitHub API. " +
        "Returns a migration readiness summary including framework version, key dependencies, Windows-specific " +
        "blockers, and recommended Azure targets (App Service, AKS, Container Apps, Azure Functions). " +
        "Works with public repos or private repos when a GitHub token is configured in settings.";

    public ScanGitHubRepoTool(
        ILogger<ScanGitHubRepoTool> logger,
        IHttpClientFactory httpClientFactory,
        CodeAnalysisService codeAnalysis,
        IOptions<GatewayOptions> gateway) : base(logger)
    {
        _httpClientFactory = httpClientFactory;
        _codeAnalysis = codeAnalysis;
        _gateway = gateway?.Value ?? new GatewayOptions();

        Parameters.Add(new ToolParameter("repo", "GitHub repository in 'owner/repo' format, e.g. 'myorg/myapp'", true));
        Parameters.Add(new ToolParameter("branch", "Branch to scan (default: main)", false));
        Parameters.Add(new ToolParameter("path", "Sub-directory path within the repo to scan (default: root)", false));
    }

    public override async Task<string> ExecuteAsync(
        IDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var repo = GetArg(arguments, "repo") ?? "";
            var branch = GetArg(arguments, "branch") ?? "main";
            var scanPath = (GetArg(arguments, "path") ?? "").TrimStart('/');

            if (string.IsNullOrWhiteSpace(repo) || !repo.Contains('/'))
                return JsonSerializer.Serialize(new { success = false, error = "repo must be in 'owner/repo' format." });

            Logger.LogInformation("Scanning GitHub repo {Repo} branch {Branch} path '{Path}'", repo, branch, scanPath);

            using var client = CreateClient();

            // Step 1: Get the file tree for the target path
            var files = await ListRepoFilesAsync(client, repo, branch, scanPath, cancellationToken);
            if (files == null)
                return JsonSerializer.Serialize(new { success = false, error = $"Could not list repository contents for '{repo}'. Check repo name, branch, and token." });

            // Step 2: Identify interesting files
            var targets = files
                .Where(f => IsSignificantFile(f))
                .Take(20) // cap to avoid huge payloads
                .ToList();

            Logger.LogInformation("Found {Count} significant files to scan in {Repo}", targets.Count, repo);

            if (!targets.Any())
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    repo,
                    branch,
                    warning = "No .NET project files (.csproj, web.config, packages.config, .sln) found. " +
                              "This may not be a .NET application, or the files are in a sub-directory — try specifying 'path'.",
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
                var content = await FetchFileContentAsync(client, repo, filePath, branch, cancellationToken);
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

            // Step 4: Synthesize a migration readiness summary
            var summary = BuildSummary(repo, branch, projectAnalyses, webConfigAnalyses, dockerfiles, solutionFiles, rawFindings);

            return JsonSerializer.Serialize(new
            {
                success = true,
                repo,
                branch,
                scanPath = string.IsNullOrEmpty(scanPath) ? "/" : scanPath,
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
            Logger.LogError(ex, "Error scanning GitHub repo");
            return JsonSerializer.Serialize(new { success = false, error = ex.Message });
        }
    }

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient();
        client.BaseAddress = new Uri("https://api.github.com");
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.Add("User-Agent", "PlatformEngineeringCopilot/1.0");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        var token = _gateway.GitHub?.AccessToken;
        if (!string.IsNullOrWhiteSpace(token))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    private async Task<List<string>?> ListRepoFilesAsync(
        HttpClient client, string repo, string branch, string path, CancellationToken ct)
    {
        try
        {
            // Use the Git Trees API (recursive) to get a flat file list efficiently
            var treesUrl = $"/repos/{repo}/git/trees/{Uri.EscapeDataString(branch)}?recursive=1";
            var resp = await client.GetAsync(treesUrl, ct);

            if (!resp.IsSuccessStatusCode)
            {
                // Try default branch fallback
                if (branch != "main")
                {
                    treesUrl = $"/repos/{repo}/git/trees/main?recursive=1";
                    resp = await client.GetAsync(treesUrl, ct);
                }
                if (!resp.IsSuccessStatusCode) return null;
            }

            var json = JsonSerializer.Deserialize<JsonElement>(await resp.Content.ReadAsStringAsync(ct));
            var tree = json.TryGetProperty("tree", out var t) ? t : default;
            if (tree.ValueKind != JsonValueKind.Array) return null;

            var files = tree.EnumerateArray()
                .Where(n => n.TryGetProperty("type", out var tp) && tp.GetString() == "blob")
                .Select(n => n.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "")
                .Where(p => string.IsNullOrEmpty(path) || p.StartsWith(path, StringComparison.OrdinalIgnoreCase))
                .ToList();

            return files;
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> FetchFileContentAsync(
        HttpClient client, string repo, string filePath, string branch, CancellationToken ct)
    {
        try
        {
            var url = $"/repos/{repo}/contents/{Uri.EscapeDataString(filePath)}?ref={Uri.EscapeDataString(branch)}";
            var resp = await client.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return null;

            var json = JsonSerializer.Deserialize<JsonElement>(await resp.Content.ReadAsStringAsync(ct));
            if (json.TryGetProperty("content", out var contentProp))
            {
                var b64 = contentProp.GetString()?.Replace("\n", "") ?? "";
                return Encoding.UTF8.GetString(Convert.FromBase64String(b64));
            }
            return null;
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
        string repo,
        string branch,
        List<object> projects,
        List<object> webConfigs,
        List<string> dockerfiles,
        List<string> solutions,
        List<string> rawFindings)
    {
        var frameworks = projects
            .Select(p => (p as dynamic)?.targetFramework as string)
            .Where(f => !string.IsNullOrEmpty(f))
            .Distinct()
            .ToList();

        var hasComReferences = projects.Any(p => ((p as dynamic)?.comReferenceCount as int? ?? 0) > 0);
        var hasWcf = webConfigs.Any(w => (w as dynamic)?.hasWcf as bool? == true);
        var hasMsmq = webConfigs.Any(w => (w as dynamic)?.hasMsmq as bool? == true);
        var isAlreadyContainerized = dockerfiles.Any();
        var totalPackages = projects.Sum(p => (p as dynamic)?.packageCount as int? ?? 0);
        var projectCount = projects.Count;

        var complexity = "Low";
        var blockers = new List<string>();
        if (hasComReferences) { blockers.Add("COM References detected — requires Windows VM or refactoring"); complexity = "High"; }
        if (hasWcf) { blockers.Add("WCF bindings detected — consider CoreWCF or gRPC migration"); complexity = complexity == "High" ? "High" : "Medium"; }
        if (hasMsmq) { blockers.Add("MSMQ usage detected — migrate to Azure Service Bus"); complexity = complexity == "High" ? "High" : "Medium"; }

        var legacyFrameworks = frameworks.Where(f =>
            f != null && (f.Contains("net3") || f.Contains("net4") || f.Contains("v3") || f.Contains("v4"))).ToList();
        if (legacyFrameworks.Any())
        {
            blockers.Add($"Legacy .NET Framework detected ({string.Join(", ", legacyFrameworks)}) — upgrade to .NET 8/9");
            if (complexity != "High") complexity = "Medium";
        }

        var recommendedTargets = new List<string>();
        if (isAlreadyContainerized) recommendedTargets.Add("Azure Kubernetes Service (already has Dockerfile)");
        else if (!hasComReferences && !hasWcf) recommendedTargets.AddRange(new[] { "Azure App Service", "Azure Container Apps" });
        if (hasWcf) recommendedTargets.Add("Azure App Service (CoreWCF)");
        if (hasComReferences) recommendedTargets.Add("Azure VM (lift-and-shift required first)");
        if (!recommendedTargets.Any()) recommendedTargets.Add("Azure App Service");

        return new
        {
            repo,
            branch,
            projectCount,
            solutionCount = solutions.Count,
            frameworks,
            totalNuGetPackages = totalPackages,
            migrationComplexity = complexity,
            blockers,
            isAlreadyContainerized,
            recommendedAzureTargets = recommendedTargets,
            additionalFindings = rawFindings
        };
    }

    private static string? GetArg(IDictionary<string, object?> args, string key) =>
        args.TryGetValue(key, out var v) ? v?.ToString() : null;
}
