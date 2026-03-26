using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Platform.Engineering.Copilot.Admin.API.DTOs;
using Platform.Engineering.Copilot.Admin.API.Services;

namespace Platform.Engineering.Copilot.Admin.API.Controllers;

/// <summary>
/// API controller for Migration &amp; Modernization.
/// Provides app assessment, migration planning, and Azure target recommendations.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class MigrationController : ControllerBase
{
    private readonly ILogger<MigrationController> _logger;
    private readonly IAzureAuthService _azureAuth;
    private static readonly JsonSerializerOptions _jsonOpts = new() { PropertyNameCaseInsensitive = true };

    // In-memory assessment store (would be DB-backed in production)
    private static readonly List<MigrationAssessmentDto> _assessments = new();
    private static readonly object _lock = new();

    public MigrationController(ILogger<MigrationController> logger, IAzureAuthService azureAuth)
    {
        _logger = logger;
        _azureAuth = azureAuth;
    }

    /// <summary>
    /// Get migration dashboard summary.
    /// </summary>
    [HttpGet("dashboard")]
    [ProducesResponseType(typeof(MigrationDashboardDto), StatusCodes.Status200OK)]
    public ActionResult<MigrationDashboardDto> GetDashboard()
    {
        lock (_lock)
        {
            var dashboard = new MigrationDashboardDto
            {
                TotalAssessments = _assessments.Count,
                ReadyToMigrate = _assessments.Count(a => a.OverallReadinessScore >= 70),
                NeedsWork = _assessments.Count(a => a.OverallReadinessScore >= 40 && a.OverallReadinessScore < 70),
                Blocked = _assessments.Count(a => a.OverallReadinessScore < 40),
                AverageReadinessScore = _assessments.Count > 0 ? _assessments.Average(a => a.OverallReadinessScore) : 0,
                RecentAssessments = _assessments.OrderByDescending(a => a.AssessedAt).Take(10).ToList()
            };
            return Ok(dashboard);
        }
    }

    /// <summary>
    /// Run a migration readiness assessment for an application.
    /// </summary>
    [HttpPost("assess")]
    [ProducesResponseType(typeof(MigrationAssessmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<MigrationAssessmentDto> RunAssessment([FromBody] MigrationAssessmentRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.ApplicationName))
            return BadRequest("Application name is required.");

        _logger.LogInformation("Running migration assessment for {AppName}", request.ApplicationName);

        var assessment = BuildAssessment(request);

        lock (_lock)
        {
            _assessments.Add(assessment);
        }

        _logger.LogInformation("Assessment complete for {AppName}: score={Score}, strategy={Strategy}",
            request.ApplicationName, assessment.OverallReadinessScore, assessment.RecommendedStrategy);

        return Ok(assessment);
    }

    /// <summary>
    /// Get Azure target service recommendations for an application.
    /// </summary>
    [HttpPost("recommendations")]
    [ProducesResponseType(typeof(TargetRecommendationsResponseDto), StatusCodes.Status200OK)]
    public ActionResult<TargetRecommendationsResponseDto> GetTargetRecommendations([FromBody] MigrationAssessmentRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.ApplicationName))
            return BadRequest("Application name is required.");

        var recommendations = BuildTargetRecommendations(request);
        return Ok(recommendations);
    }

    /// <summary>
    /// Get all assessments.
    /// </summary>
    [HttpGet("assessments")]
    [ProducesResponseType(typeof(List<MigrationAssessmentDto>), StatusCodes.Status200OK)]
    public ActionResult<List<MigrationAssessmentDto>> GetAssessments()
    {
        lock (_lock)
        {
            return Ok(_assessments.OrderByDescending(a => a.AssessedAt).ToList());
        }
    }

    /// <summary>
    /// Get a specific assessment by ID.
    /// </summary>
    [HttpGet("assessments/{id}")]
    [ProducesResponseType(typeof(MigrationAssessmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<MigrationAssessmentDto> GetAssessment(string id)
    {
        lock (_lock)
        {
            var assessment = _assessments.FirstOrDefault(a => a.Id == id);
            if (assessment == null)
                return NotFound();
            return Ok(assessment);
        }
    }

    /// <summary>
    /// Delete an assessment.
    /// </summary>
    [HttpDelete("assessments/{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult DeleteAssessment(string id)
    {
        lock (_lock)
        {
            var assessment = _assessments.FirstOrDefault(a => a.Id == id);
            if (assessment == null)
                return NotFound();
            _assessments.Remove(assessment);
            return NoContent();
        }
    }

    /// <summary>
    /// Check Azure Migrate project availability for the subscription.
    /// </summary>
    [HttpGet("azure-migrate-status")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAzureMigrateStatus()
    {
        var settings = IntegrationsController.LoadSettings();
        var azureSettings = settings.Azure;

        if (azureSettings == null || string.IsNullOrWhiteSpace(azureSettings.SubscriptionId))
        {
            return Ok(new { status = "not_configured", message = "Azure integration is not configured." });
        }

        var armEndpoint = _azureAuth.GetArmEndpoint(azureSettings.CloudEnvironment);
        string accessToken;
        try
        {
            accessToken = await _azureAuth.GetAccessTokenAsync(azureSettings, armEndpoint);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to acquire Azure token for Migration status");
            return Ok(new { status = "auth_error", message = $"Authentication failed: {ex.Message}" });
        }

        try
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            // Query Azure Migrate projects via ARM
            var url = $"{armEndpoint}/subscriptions/{azureSettings.SubscriptionId}/providers/Microsoft.Migrate/migrateProjects?api-version=2020-06-01-preview";
            var response = await httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var projects = doc.RootElement.GetProperty("value").GetArrayLength();
                return Ok(new { status = "ok", projectCount = projects, message = $"Found {projects} Azure Migrate project(s)." });
            }

            // Migrate may not be registered in the subscription
            return Ok(new { status = "not_available", message = "Azure Migrate is not configured in this subscription. You can still run local assessments." });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query Azure Migrate projects");
            return Ok(new { status = "error", message = ex.Message });
        }
    }

    #region Assessment Logic

    private MigrationAssessmentDto BuildAssessment(MigrationAssessmentRequestDto request)
    {
        var assessment = new MigrationAssessmentDto
        {
            ApplicationName = request.ApplicationName
        };

        // Score framework compatibility
        assessment.FrameworkCompatibilityScore = ScoreFramework(request.FrameworkVersion);

        // Score dependency health (from project file if provided)
        assessment.DependencyHealthScore = ScoreDependencies(request.ProjectFileContent);

        // Score database coupling
        assessment.DatabaseCouplingScore = ScoreDatabase(request.DatabaseType);

        // Score security posture
        assessment.SecurityPostureScore = ScoreSecurity(request.HostingEnvironment);

        // Calculate overall score (weighted average)
        assessment.OverallReadinessScore = (int)(
            assessment.FrameworkCompatibilityScore * 0.35 +
            assessment.DependencyHealthScore * 0.25 +
            assessment.DatabaseCouplingScore * 0.20 +
            assessment.SecurityPostureScore * 0.20);

        // Determine strategy
        assessment.RecommendedStrategy = assessment.OverallReadinessScore switch
        {
            >= 80 => "Rehost",
            >= 50 => "Replatform",
            _ => "Refactor"
        };

        // Recommend Azure target
        assessment.RecommendedAzureService = RecommendTarget(request, assessment);

        // Identify blockers
        assessment.Blockers = IdentifyBlockers(request, assessment);

        // Generate recommendations
        assessment.Recommendations = GenerateRecommendations(request, assessment);

        // Build migration phases
        assessment.MigrationPhases = BuildPhases(assessment);

        return assessment;
    }

    private int ScoreFramework(string? frameworkVersion)
    {
        if (string.IsNullOrWhiteSpace(frameworkVersion)) return 50;
        var fw = frameworkVersion.ToLowerInvariant().Trim();

        if (fw.Contains("net9") || fw.Contains("net 9") || fw.Contains(".net 9")) return 95;
        if (fw.Contains("net8") || fw.Contains("net 8") || fw.Contains(".net 8")) return 92;
        if (fw.Contains("net7") || fw.Contains("net 7") || fw.Contains(".net 7")) return 88;
        if (fw.Contains("net6") || fw.Contains("net 6") || fw.Contains(".net 6")) return 85;
        if (fw.Contains("net5") || fw.Contains("net 5") || fw.Contains(".net 5")) return 75;
        if (fw.Contains("core 3") || fw.Contains("netcoreapp3")) return 70;
        if (fw.Contains("core 2") || fw.Contains("netcoreapp2")) return 60;
        if (fw.Contains("4.8")) return 50;
        if (fw.Contains("4.7")) return 45;
        if (fw.Contains("4.6")) return 40;
        if (fw.Contains("4.5")) return 35;
        if (fw.Contains("3.5") || fw.Contains("2.0") || fw.Contains("1.1")) return 20;

        return 50;
    }

    private int ScoreDependencies(string? projectContent)
    {
        if (string.IsNullOrWhiteSpace(projectContent)) return 65;

        var score = 80;
        var lower = projectContent.ToLowerInvariant();

        // Known problematic dependencies
        if (lower.Contains("system.web")) score -= 15;
        if (lower.Contains("microsoft.aspnet.webforms")) score -= 20;
        if (lower.Contains("wcf") || lower.Contains("servicemodel")) score -= 15;
        if (lower.Contains("com interop") || lower.Contains("comreference")) score -= 20;
        if (lower.Contains("system.enterpriseservices")) score -= 15;
        if (lower.Contains("crystal reports") || lower.Contains("crystaldecisions")) score -= 10;

        // Positive indicators
        if (lower.Contains("microsoft.aspnetcore")) score += 10;
        if (lower.Contains("microsoft.extensions.dependencyinjection")) score += 5;

        return Math.Clamp(score, 0, 100);
    }

    private int ScoreDatabase(string? databaseType)
    {
        if (string.IsNullOrWhiteSpace(databaseType)) return 60;
        var db = databaseType.ToLowerInvariant().Trim();

        if (db.Contains("sql server") || db.Contains("azure sql") || db.Contains("mssql")) return 90;
        if (db.Contains("postgresql") || db.Contains("postgres")) return 85;
        if (db.Contains("mysql") || db.Contains("mariadb")) return 80;
        if (db.Contains("cosmos") || db.Contains("mongodb")) return 85;
        if (db.Contains("oracle")) return 45;
        if (db.Contains("access") || db.Contains("mdb")) return 25;
        if (db == "none" || db == "n/a") return 95;

        return 60;
    }

    private int ScoreSecurity(string? hostingEnvironment)
    {
        if (string.IsNullOrWhiteSpace(hostingEnvironment)) return 60;
        var env = hostingEnvironment.ToLowerInvariant().Trim();

        if (env.Contains("azure") || env.Contains("cloud")) return 85;
        if (env.Contains("container") || env.Contains("docker") || env.Contains("kubernetes")) return 80;
        if (env.Contains("iis") && env.Contains("windows server 2022")) return 70;
        if (env.Contains("iis") && env.Contains("windows server 2019")) return 65;
        if (env.Contains("iis")) return 55;
        if (env.Contains("windows server 2012") || env.Contains("windows server 2008")) return 35;

        return 60;
    }

    private string RecommendTarget(MigrationAssessmentRequestDto request, MigrationAssessmentDto assessment)
    {
        if (!string.IsNullOrWhiteSpace(request.TargetService))
            return request.TargetService;

        return assessment.RecommendedStrategy switch
        {
            "Rehost" => "Azure App Service",
            "Replatform" => "Azure Container Apps",
            _ => "Azure Kubernetes Service (AKS)"
        };
    }

    private List<string> IdentifyBlockers(MigrationAssessmentRequestDto request, MigrationAssessmentDto assessment)
    {
        var blockers = new List<string>();

        if (assessment.FrameworkCompatibilityScore < 30)
            blockers.Add("Legacy .NET Framework version requires significant upgrade effort before migration.");

        if (assessment.DependencyHealthScore < 30)
            blockers.Add("Critical dependencies on legacy libraries (COM Interop, WebForms, WCF) must be resolved.");

        if (assessment.DatabaseCouplingScore < 30)
            blockers.Add("Database technology has limited Azure Government support — requires migration plan.");

        if (!string.IsNullOrWhiteSpace(request.HostingEnvironment) &&
            request.HostingEnvironment.ToLowerInvariant().Contains("2008"))
            blockers.Add("Windows Server 2008 is end-of-life — OS upgrade required before cloud migration.");

        return blockers;
    }

    private List<string> GenerateRecommendations(MigrationAssessmentRequestDto request, MigrationAssessmentDto assessment)
    {
        var recs = new List<string>();

        if (assessment.FrameworkCompatibilityScore < 70)
            recs.Add("Upgrade to .NET 8 or .NET 9 for best Azure App Service / Container Apps compatibility.");

        if (assessment.DependencyHealthScore < 70)
            recs.Add("Replace legacy dependencies (System.Web, WebForms) with ASP.NET Core equivalents.");

        if (assessment.DatabaseCouplingScore < 70 && !string.IsNullOrWhiteSpace(request.DatabaseType))
            recs.Add($"Plan database migration: {request.DatabaseType} → Azure SQL or Cosmos DB.");

        recs.Add("Use Azure App Service Migration Assistant for automated readiness checks.");
        recs.Add("Enable Azure Policy for NIST 800-53 compliance from day one.");
        recs.Add("Configure Azure Monitor and Application Insights for observability post-migration.");

        if (assessment.RecommendedStrategy == "Replatform" || assessment.RecommendedStrategy == "Refactor")
            recs.Add("Containerize the application using Docker before deploying to Azure Container Apps or AKS.");

        return recs;
    }

    private List<MigrationPhaseDto> BuildPhases(MigrationAssessmentDto assessment)
    {
        var phases = new List<MigrationPhaseDto>
        {
            new()
            {
                Phase = 1,
                Name = "Assessment & Planning",
                Description = "Evaluate application readiness and create detailed migration plan.",
                EstimatedEffort = assessment.OverallReadinessScore >= 70 ? "1-2 weeks" : "2-4 weeks",
                Tasks = new()
                {
                    "Run Azure App Service Migration Assistant",
                    "Inventory all dependencies and integrations",
                    "Document current infrastructure configuration",
                    "Define target Azure architecture",
                    "Create risk mitigation plan"
                }
            },
            new()
            {
                Phase = 2,
                Name = "Preparation & Modernization",
                Description = "Address blockers and prepare the application for cloud deployment.",
                EstimatedEffort = assessment.RecommendedStrategy == "Rehost" ? "1-2 weeks" : "4-8 weeks",
                Tasks = new()
                {
                    "Resolve identified blockers",
                    "Upgrade framework/dependencies as needed",
                    "Externalize configuration (connection strings, secrets)",
                    "Set up Azure Key Vault for secrets management",
                    "Create CI/CD pipeline for automated deployment"
                }
            },
            new()
            {
                Phase = 3,
                Name = "Migration & Deployment",
                Description = $"Deploy application to {assessment.RecommendedAzureService}.",
                EstimatedEffort = "1-3 weeks",
                Tasks = new()
                {
                    $"Provision {assessment.RecommendedAzureService} in Azure Government",
                    "Deploy application to staging environment",
                    "Migrate database to Azure",
                    "Configure networking and security (NSGs, private endpoints)",
                    "Run smoke tests and validate functionality"
                }
            },
            new()
            {
                Phase = 4,
                Name = "Validation & Cutover",
                Description = "Validate compliance, performance, and complete the migration.",
                EstimatedEffort = "1-2 weeks",
                Tasks = new()
                {
                    "Run compliance scan (NIST 800-53 / FedRAMP)",
                    "Performance and load testing",
                    "DNS cutover and traffic routing",
                    "Monitor for issues post-migration",
                    "Decommission on-premises infrastructure"
                }
            }
        };

        return phases;
    }

    #endregion

    #region Target Recommendations

    private TargetRecommendationsResponseDto BuildTargetRecommendations(MigrationAssessmentRequestDto request)
    {
        var fw = request.FrameworkVersion?.ToLowerInvariant() ?? "";
        var isModern = fw.Contains("net6") || fw.Contains("net7") || fw.Contains("net8") || fw.Contains("net9") || fw.Contains("core");

        var response = new TargetRecommendationsResponseDto
        {
            ApplicationName = request.ApplicationName,
            Recommendations = new()
            {
                new()
                {
                    TargetService = "Azure App Service",
                    Rationale = "Fully managed PaaS for web applications. Best for straightforward web apps with minimal infrastructure management.",
                    CompatibilityScore = isModern ? 95 : 65,
                    EstimatedMonthlyCost = 73.00m,
                    SupportsGovCloud = true,
                    Pros = new() { "Zero infrastructure management", "Built-in auto-scaling", "Deployment slots for zero-downtime", "FedRAMP High authorized" },
                    Cons = new() { "Limited control over underlying OS", "No custom container networking" }
                },
                new()
                {
                    TargetService = "Azure Container Apps",
                    Rationale = "Serverless containers for microservices. Ideal for containerized apps that need auto-scaling and Dapr integration.",
                    CompatibilityScore = isModern ? 90 : 55,
                    EstimatedMonthlyCost = 50.00m,
                    SupportsGovCloud = true,
                    Pros = new() { "Pay for what you use (scale to zero)", "Built-in Dapr support", "KEDA-based auto-scaling", "Simpler than AKS" },
                    Cons = new() { "Less control than AKS", "Requires containerization" }
                },
                new()
                {
                    TargetService = "Azure Kubernetes Service (AKS)",
                    Rationale = "Managed Kubernetes for complex multi-container workloads with full orchestration control.",
                    CompatibilityScore = isModern ? 88 : 60,
                    EstimatedMonthlyCost = 200.00m,
                    SupportsGovCloud = true,
                    Pros = new() { "Full Kubernetes ecosystem", "Maximum flexibility", "Enterprise-grade networking", "DoD IL5 supported" },
                    Cons = new() { "Higher operational complexity", "Requires Kubernetes expertise", "Higher base cost" }
                },
                new()
                {
                    TargetService = "Azure Functions",
                    Rationale = "Serverless compute for event-driven workloads. Best for background tasks, APIs, and batch processing.",
                    CompatibilityScore = isModern ? 70 : 40,
                    EstimatedMonthlyCost = 25.00m,
                    SupportsGovCloud = true,
                    Pros = new() { "True serverless (pay per execution)", "Event-driven triggers", "Lowest cost for intermittent workloads" },
                    Cons = new() { "Cold start latency", "Execution time limits", "Requires code restructuring" }
                }
            }
        };

        return response;
    }

    #endregion
}
