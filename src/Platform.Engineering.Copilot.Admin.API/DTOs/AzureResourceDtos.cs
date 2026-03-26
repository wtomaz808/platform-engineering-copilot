namespace Platform.Engineering.Copilot.Admin.API.DTOs;

public class AzureResourceOverviewDto
{
    public string Status { get; set; } = "not_configured";
    public string? Message { get; set; }
    public TenantInfoDto TenantInfo { get; set; } = new();
    public SubscriptionInfoDto SubscriptionInfo { get; set; } = new();
    public List<ManagementGroupDto> ManagementGroups { get; set; } = new();
    public List<PolicyAssignmentDto> Policies { get; set; } = new();
    public List<ResourceGroupDto> ResourceGroups { get; set; } = new();
    public EntraIdInfoDto EntraIdInfo { get; set; } = new();
}

public class TenantInfoDto
{
    public string TenantId { get; set; } = "";
    public string CloudEnvironment { get; set; } = "";
    public string AuthMethod { get; set; } = "";
    public string? TenantDisplayName { get; set; }
}

public class SubscriptionInfoDto
{
    public string SubscriptionId { get; set; } = "";
    public string? DisplayName { get; set; }
    public string? State { get; set; }
}

public class ManagementGroupDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
}

public class PolicyAssignmentDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string EnforcementMode { get; set; } = "";
}

public class ResourceGroupDto
{
    public string Name { get; set; } = "";
    public string Location { get; set; } = "";
    public string ProvisioningState { get; set; } = "";
}

public class EntraIdInfoDto
{
    public string? TenantDisplayName { get; set; }
    public int? UserCount { get; set; }
    public int? GroupCount { get; set; }
    public int? AppRegistrationCount { get; set; }
}
