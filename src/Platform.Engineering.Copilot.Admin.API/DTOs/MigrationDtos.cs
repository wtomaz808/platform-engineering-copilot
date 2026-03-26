namespace Platform.Engineering.Copilot.Admin.API.DTOs;

public class MigrationAssessmentRequestDto
{
    public string ApplicationName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? FrameworkVersion { get; set; }
    public string? ProjectFileContent { get; set; }
    public string? HostingEnvironment { get; set; }
    public string? DatabaseType { get; set; }
    public string? TargetService { get; set; }
}

public class MigrationAssessmentDto
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string ApplicationName { get; set; } = string.Empty;
    public string Status { get; set; } = "ok";
    public string? Message { get; set; }
    public DateTime AssessedAt { get; set; } = DateTime.UtcNow;

    // Readiness scores (0-100)
    public int OverallReadinessScore { get; set; }
    public int FrameworkCompatibilityScore { get; set; }
    public int DependencyHealthScore { get; set; }
    public int DatabaseCouplingScore { get; set; }
    public int SecurityPostureScore { get; set; }

    public string RecommendedStrategy { get; set; } = string.Empty;  // Rehost / Replatform / Refactor
    public string RecommendedAzureService { get; set; } = string.Empty;

    public List<string> Blockers { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();
    public List<MigrationPhaseDto> MigrationPhases { get; set; } = new();
}

public class MigrationPhaseDto
{
    public int Phase { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string EstimatedEffort { get; set; } = string.Empty;
    public List<string> Tasks { get; set; } = new();
}

public class MigrationDashboardDto
{
    public string Status { get; set; } = "ok";
    public string? Message { get; set; }
    public int TotalAssessments { get; set; }
    public int ReadyToMigrate { get; set; }
    public int NeedsWork { get; set; }
    public int Blocked { get; set; }
    public double AverageReadinessScore { get; set; }
    public List<MigrationAssessmentDto> RecentAssessments { get; set; } = new();
}

public class AzureTargetRecommendationDto
{
    public string TargetService { get; set; } = string.Empty;
    public string Rationale { get; set; } = string.Empty;
    public int CompatibilityScore { get; set; }
    public decimal EstimatedMonthlyCost { get; set; }
    public List<string> Pros { get; set; } = new();
    public List<string> Cons { get; set; } = new();
    public bool SupportsGovCloud { get; set; }
}

public class TargetRecommendationsResponseDto
{
    public string Status { get; set; } = "ok";
    public string? Message { get; set; }
    public string ApplicationName { get; set; } = string.Empty;
    public List<AzureTargetRecommendationDto> Recommendations { get; set; } = new();
}
