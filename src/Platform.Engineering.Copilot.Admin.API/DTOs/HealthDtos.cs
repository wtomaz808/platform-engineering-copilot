namespace Platform.Engineering.Copilot.Admin.API.DTOs;

public class ResourceHealthStatusDto
{
    public string ResourceId { get; set; } = "";
    public string ResourceName { get; set; } = "";
    public string ResourceType { get; set; } = "";
    public string ResourceGroup { get; set; } = "";
    public string Location { get; set; } = "";
    public string AvailabilityState { get; set; } = "Unknown";
    public string? Summary { get; set; }
    public string? ReasonType { get; set; }
    public DateTime? OccurredTime { get; set; }
    public DateTime? ReportedTime { get; set; }
}

public class ResourceHealthResponseDto
{
    public string Status { get; set; } = "ok";
    public string? Message { get; set; }
    public string SubscriptionId { get; set; } = "";
    public int TotalResources { get; set; }
    public int AvailableCount { get; set; }
    public int DegradedCount { get; set; }
    public int UnavailableCount { get; set; }
    public int UnknownCount { get; set; }
    public List<ResourceHealthStatusDto> Resources { get; set; } = new();
}

public class ServiceHealthAlertDto
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string EventType { get; set; } = "";
    public string Status { get; set; } = "";
    public string Level { get; set; } = "";
    public string? ImpactedService { get; set; }
    public string? ImpactedRegion { get; set; }
    public string? Description { get; set; }
    public DateTime? LastModifiedTime { get; set; }
    public DateTime? ImpactStartTime { get; set; }
}

public class ServiceHealthResponseDto
{
    public string Status { get; set; } = "ok";
    public string? Message { get; set; }
    public string SubscriptionId { get; set; } = "";
    public int TotalAlerts { get; set; }
    public List<ServiceHealthAlertDto> Alerts { get; set; } = new();
}
