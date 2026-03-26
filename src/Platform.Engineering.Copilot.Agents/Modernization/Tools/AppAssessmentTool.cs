using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Platform.Engineering.Copilot.Agents.Common;
using Platform.Engineering.Copilot.Agents.Modernization.Services;

namespace Platform.Engineering.Copilot.Agents.Modernization.Tools;

/// <summary>
/// Assesses a .NET application's migration readiness by analyzing project files,
/// web.config, dependencies, and framework compatibility. Produces readiness scores
/// and classifies migration strategy (Rehost, Replatform, Refactor).
/// </summary>
public class AppAssessmentTool : BaseTool
{
    private readonly CodeAnalysisService _codeAnalysis;

    public override string Name => "app_assessment";

    public override string Description =>
        "Assess a .NET application's migration readiness for Azure Government. Analyzes project files (.csproj), " +
        "web.config, NuGet dependencies, IIS modules, WCF bindings, COM references, and connection strings. " +
        "Produces readiness scores and recommends migration strategy (Rehost/Replatform/Refactor). " +
        "Input can be pasted file content, a description of the app, or answers to assessment questions.";

    public AppAssessmentTool(
        ILogger<AppAssessmentTool> logger,
        CodeAnalysisService codeAnalysis) : base(logger)
    {
        _codeAnalysis = codeAnalysis;
        Parameters.Add(new ToolParameter("input", "Project file content (.csproj, web.config, packages.config), or natural language description of the application including .NET version, dependencies, IIS usage, database type, and hosting environment.", true));
    }

    public override async Task<string> ExecuteAsync(
        IDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var input = GetRequiredString(arguments, "input");
        Logger.LogInformation("Running app assessment on provided input ({Length} chars)", input.Length);

        var assessment = new AppAssessmentResult();

        // Try to detect and parse structured content
        if (input.TrimStart().StartsWith("<") && (input.Contains("<Project") || input.Contains("<VisualStudioProject")))
        {
            var projectAnalysis = _codeAnalysis.ParseCsprojContent(input);
            assessment.FrameworkVersion = projectAnalysis.TargetFramework;
            assessment.IsSdkStyle = projectAnalysis.IsSdkStyle;
            assessment.PackageCount = projectAnalysis.PackageReferences.Count;
            assessment.ComReferenceCount = projectAnalysis.ComReferences.Count;
            assessment.AssemblyReferenceCount = projectAnalysis.AssemblyReferences.Count;
            assessment.ProjectReferenceCount = projectAnalysis.ProjectReferences.Count;
            assessment.OutputType = projectAnalysis.OutputType;
            assessment.Packages = projectAnalysis.PackageReferences.Select(p => $"{p.Name} {p.Version}").ToList();
            assessment.ComReferences = projectAnalysis.ComReferences;

            // Score framework compatibility
            assessment.FrameworkCompatibilityScore = ScoreFrameworkCompatibility(projectAnalysis.TargetFramework);

            // Score dependency health
            assessment.DependencyHealthScore = ScoreDependencyHealth(projectAnalysis);

            // Flag Windows-only patterns
            if (projectAnalysis.ComReferences.Count > 0) assessment.WindowsDependencies.Add("COM References: " + string.Join(", ", projectAnalysis.ComReferences));

            assessment.ParsedFromFile = true;
        }
        else if (input.TrimStart().StartsWith("<") && (input.Contains("<configuration") || input.Contains("<connectionStrings")))
        {
            var webConfig = _codeAnalysis.ParseWebConfigContent(input);
            assessment.ConnectionStrings = webConfig.ConnectionStrings.Select(c => $"{c.Name} ({c.DetectedDbType})").ToList();
            assessment.HasWcf = webConfig.HasWcf;
            assessment.HasMsmq = webConfig.HasMsmq;
            assessment.IisModuleCount = webConfig.IisModules.Count;
            assessment.AuthenticationMode = webConfig.AuthenticationMode;
            assessment.WcfServices = webConfig.WcfServices;
            assessment.IisDependencyScore = ScoreIisDependency(webConfig);
            assessment.DatabaseCouplingScore = webConfig.ConnectionStrings.Count > 0 ? 40 + (webConfig.ConnectionStrings.Count * 10) : 90;

            if (webConfig.HasWcf) assessment.WindowsDependencies.Add($"WCF Services: {string.Join(", ", webConfig.WcfServices)}");
            if (webConfig.HasMsmq) assessment.WindowsDependencies.Add("MSMQ messaging detected");
            if (webConfig.CompilationTargetFramework != null) assessment.FrameworkVersion = webConfig.CompilationTargetFramework;

            assessment.ParsedFromFile = true;
        }
        else if (input.TrimStart().StartsWith("<") && input.Contains("<packages"))
        {
            var packages = _codeAnalysis.ParsePackagesConfig(input);
            assessment.PackageCount = packages.Count;
            assessment.Packages = packages.Select(p => $"{p.Name} {p.Version}").ToList();
            assessment.ParsedFromFile = true;
        }

        // Calculate overall scores if we parsed files
        if (assessment.ParsedFromFile)
        {
            assessment.OverallReadinessScore = CalculateOverallScore(assessment);
            assessment.RecommendedStrategy = DetermineStrategy(assessment);
            assessment.Blockers = IdentifyBlockers(assessment);
            assessment.Recommendations = GenerateRecommendations(assessment);
        }

        return ToJson(new
        {
            success = true,
            tool = "app_assessment",
            parsedFromFile = assessment.ParsedFromFile,
            assessment = new
            {
                frameworkVersion = assessment.FrameworkVersion,
                isSdkStyle = assessment.IsSdkStyle,
                outputType = assessment.OutputType,
                scores = new
                {
                    overall = assessment.OverallReadinessScore,
                    frameworkCompatibility = assessment.FrameworkCompatibilityScore,
                    dependencyHealth = assessment.DependencyHealthScore,
                    iisDependency = assessment.IisDependencyScore,
                    databaseCoupling = assessment.DatabaseCouplingScore
                },
                recommendedStrategy = assessment.RecommendedStrategy,
                blockers = assessment.Blockers,
                windowsDependencies = assessment.WindowsDependencies,
                packages = assessment.Packages.Take(30).ToList(),
                comReferences = assessment.ComReferences,
                connectionStrings = assessment.ConnectionStrings,
                wcfServices = assessment.WcfServices,
                hasWcf = assessment.HasWcf,
                hasMsmq = assessment.HasMsmq,
                authenticationMode = assessment.AuthenticationMode,
                recommendations = assessment.Recommendations
            },
            userInput = input,
            instructions = !assessment.ParsedFromFile
                ? "I could not detect a structured project file in the input. I will analyze your description using AI. " +
                  "For the best results, paste your .csproj, web.config, or packages.config file content."
                : null
        });
    }

    private int ScoreFrameworkCompatibility(string framework)
    {
        var lower = framework.ToLowerInvariant();
        if (lower.Contains("net9") || lower.Contains("net8") || lower.Contains("net7")) return 95;
        if (lower.Contains("net6")) return 90;
        if (lower.Contains("net5") || lower.Contains("netcoreapp3")) return 80;
        if (lower.Contains("netcoreapp2")) return 70;
        if (lower.Contains("netstandard")) return 75;
        if (lower.Contains("v4.8") || lower.Contains("net48")) return 50;
        if (lower.Contains("v4.7") || lower.Contains("net47")) return 45;
        if (lower.Contains("v4.6") || lower.Contains("net46")) return 40;
        if (lower.Contains("v4.5") || lower.Contains("net45")) return 35;
        if (lower.Contains("v4.0") || lower.Contains("net40")) return 25;
        if (lower.Contains("v3.5") || lower.Contains("net35")) return 15;
        return 30; // Unknown
    }

    private int ScoreDependencyHealth(CodeAnalysisService.ProjectAnalysis project)
    {
        var score = 90;
        if (project.ComReferences.Count > 0) score -= project.ComReferences.Count * 15;
        if (project.AssemblyReferences.Any(a => a.HintPath?.Contains("GAC") == true)) score -= 20;
        // Legacy packages penalty
        var legacyPkgs = project.PackageReferences.Count(p =>
            p.Name.StartsWith("System.Web", StringComparison.OrdinalIgnoreCase) ||
            p.Name.Contains("WebPages", StringComparison.OrdinalIgnoreCase));
        score -= legacyPkgs * 10;
        return Math.Max(0, Math.Min(100, score));
    }

    private int ScoreIisDependency(CodeAnalysisService.WebConfigAnalysis webConfig)
    {
        var score = 90;
        if (webConfig.IisModules.Count > 5) score -= 20;
        if (webConfig.IisHandlers.Count > 3) score -= 10;
        if (webConfig.HasWcf) score -= 25;
        if (webConfig.HasMsmq) score -= 20;
        if (webConfig.AuthenticationMode == "Windows") score -= 15;
        return Math.Max(0, Math.Min(100, score));
    }

    private int CalculateOverallScore(AppAssessmentResult a)
    {
        var scores = new List<int>();
        if (a.FrameworkCompatibilityScore > 0) scores.Add(a.FrameworkCompatibilityScore);
        if (a.DependencyHealthScore > 0) scores.Add(a.DependencyHealthScore);
        if (a.IisDependencyScore > 0) scores.Add(a.IisDependencyScore);
        if (a.DatabaseCouplingScore > 0) scores.Add(a.DatabaseCouplingScore);
        return scores.Count > 0 ? (int)scores.Average() : 50;
    }

    private string DetermineStrategy(AppAssessmentResult a)
    {
        if (a.OverallReadinessScore >= 75) return "Rehost (lift-and-shift to App Service)";
        if (a.OverallReadinessScore >= 45) return "Replatform (containerize with minimal changes)";
        return "Refactor (rewrite/modernize to .NET 8/9)";
    }

    private List<string> IdentifyBlockers(AppAssessmentResult a)
    {
        var blockers = new List<string>();
        if (a.ComReferenceCount > 0) blockers.Add($"{a.ComReferenceCount} COM reference(s) — require Windows containers or removal");
        if (a.HasWcf) blockers.Add("WCF services detected — must migrate to gRPC, REST APIs, or CoreWCF");
        if (a.HasMsmq) blockers.Add("MSMQ detected — must migrate to Azure Service Bus");
        if (a.FrameworkCompatibilityScore < 30) blockers.Add($"Legacy framework ({a.FrameworkVersion}) — requires .NET upgrade");
        return blockers;
    }

    private List<string> GenerateRecommendations(AppAssessmentResult a)
    {
        var recs = new List<string>();
        if (a.FrameworkCompatibilityScore < 50)
            recs.Add($"Upgrade from {a.FrameworkVersion} to .NET 8 or 9 using the .NET Upgrade Assistant");
        if (a.HasWcf)
            recs.Add("Migrate WCF services to CoreWCF (drop-in) or rewrite as ASP.NET Core REST/gRPC services");
        if (a.HasMsmq)
            recs.Add("Replace MSMQ with Azure Service Bus for cloud-native messaging");
        if (a.ComReferenceCount > 0)
            recs.Add("Evaluate COM dependencies — replace with .NET native libraries or use Windows containers");
        if (a.IisDependencyScore < 50)
            recs.Add("High IIS coupling — consider containerization with Windows Server Core base image");
        recs.Add("Run the Azure App Service Migration Assistant for detailed compatibility check");
        recs.Add("Set up GitHub Advanced Security or SonarQube in your CI/CD pipeline for ongoing code scanning");
        return recs;
    }

    private class AppAssessmentResult
    {
        public bool ParsedFromFile { get; set; }
        public string FrameworkVersion { get; set; } = "Unknown";
        public bool IsSdkStyle { get; set; }
        public string OutputType { get; set; } = "";
        public int PackageCount { get; set; }
        public int ComReferenceCount { get; set; }
        public int AssemblyReferenceCount { get; set; }
        public int ProjectReferenceCount { get; set; }
        public int IisModuleCount { get; set; }
        public int OverallReadinessScore { get; set; } = 50;
        public int FrameworkCompatibilityScore { get; set; }
        public int DependencyHealthScore { get; set; } = 80;
        public int IisDependencyScore { get; set; } = 80;
        public int DatabaseCouplingScore { get; set; } = 80;
        public string RecommendedStrategy { get; set; } = "";
        public string AuthenticationMode { get; set; } = "";
        public bool HasWcf { get; set; }
        public bool HasMsmq { get; set; }
        public List<string> Blockers { get; set; } = new();
        public List<string> WindowsDependencies { get; set; } = new();
        public List<string> Packages { get; set; } = new();
        public List<string> ComReferences { get; set; } = new();
        public List<string> ConnectionStrings { get; set; } = new();
        public List<string> WcfServices { get; set; } = new();
        public List<string> Recommendations { get; set; } = new();
    }
}
