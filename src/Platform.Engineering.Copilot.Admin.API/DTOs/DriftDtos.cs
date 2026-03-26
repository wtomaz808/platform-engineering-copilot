namespace Platform.Engineering.Copilot.Admin.API.DTOs;

public class DriftScanResponseDto
{
    public string Status { get; set; } = "ok";
    public string? Message { get; set; }
    public string? SubscriptionId { get; set; }
    public DateTime ScannedAt { get; set; } = DateTime.UtcNow;
    public int TotalResources { get; set; }
    public int DriftedResources { get; set; }
    public int CompliantResources { get; set; }
    public List<DriftedResourceDto> DriftItems { get; set; } = new();
}

public class DriftedResourceDto
{
    public string ResourceId { get; set; } = string.Empty;
    public string ResourceName { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public string ResourceGroup { get; set; } = string.Empty;
    public string DriftType { get; set; } = string.Empty; // PolicyViolation, TagMissing, ConfigDrift
    public string Severity { get; set; } = "Warning"; // Critical, Warning, Info
    public string PropertyPath { get; set; } = string.Empty;
    public string? ExpectedValue { get; set; }
    public string? ActualValue { get; set; }
    public string? PolicyName { get; set; }
    public string? PolicyDefinitionId { get; set; }
}
