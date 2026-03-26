namespace Platform.Engineering.Copilot.Admin.API.DTOs;

public class CostSummaryDto
{
    public string Status { get; set; } = "ok";
    public string? Message { get; set; }
    public string? SubscriptionId { get; set; }
    public DateTime QueryDate { get; set; } = DateTime.UtcNow;
    public decimal TotalCostLast30Days { get; set; }
    public string Currency { get; set; } = "USD";
    public List<DailyCostDto> DailyCosts { get; set; } = new();
    public List<ServiceCostDto> CostByService { get; set; } = new();
}

public class CostByResourceGroupDto
{
    public string Status { get; set; } = "ok";
    public string? Message { get; set; }
    public string? SubscriptionId { get; set; }
    public List<ResourceGroupCostDto> ResourceGroups { get; set; } = new();
}

public class DailyCostDto
{
    public DateTime Date { get; set; }
    public decimal Cost { get; set; }
    public string Currency { get; set; } = "USD";
}

public class ServiceCostDto
{
    public string ServiceName { get; set; } = string.Empty;
    public decimal Cost { get; set; }
    public string Currency { get; set; } = "USD";
}

public class ResourceGroupCostDto
{
    public string ResourceGroupName { get; set; } = string.Empty;
    public decimal Cost { get; set; }
    public string Currency { get; set; } = "USD";
    public int ResourceCount { get; set; }
}
