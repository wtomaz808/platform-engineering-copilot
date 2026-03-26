namespace Platform.Engineering.Copilot.Admin.Client.Models;

#region Template Models

public class TemplateListItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int DeploymentCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    
    // Git Sync properties
    public bool HasGitSource { get; set; }
    public string? GitRepositoryUrl { get; set; }
    public DateTime? LastSyncedFromGit { get; set; }
    public bool GitAutoSync { get; set; }
}

public class TemplateDetail
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public string TemplateContent { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    
    /// <summary>
    /// Deployment scope: "resourceGroup" or "subscription"
    /// </summary>
    public string DeploymentScope { get; set; } = "resourceGroup";
    
    public bool RequiresApproval { get; set; }
    public bool EnforceCompliance { get; set; } = true;
    public int? DefaultExpirationDays { get; set; }
    public List<string> ComplianceFrameworks { get; set; } = new();
    public List<string> Keywords { get; set; } = new();
    public List<string> UseCases { get; set; } = new();
    public string? AiSelectionHint { get; set; }
    public int DeploymentCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public ApprovalInfo? Approval { get; set; }
    public List<TemplateParameter> Parameters { get; set; } = new();
    public List<TemplateGuardrail> Guardrails { get; set; } = new();
    
    /// <summary>
    /// Additional template files (e.g., Bicep modules) synced from Git.
    /// </summary>
    public List<TemplateFileInfo> AdditionalFiles { get; set; } = new();
    
    // Git Sync properties
    public bool HasGitSource { get; set; }
    public string? GitRepositoryUrl { get; set; }
    public string? GitBranch { get; set; }
    public string? GitPath { get; set; }
    public string? GitCommitSha { get; set; }
    public DateTime? LastSyncedFromGit { get; set; }
    public bool GitAutoSync { get; set; }
    public int GitSyncIntervalMinutes { get; set; }
}

/// <summary>
/// Additional file info for Bicep modules
/// </summary>
public class TemplateFileInfo
{
    public string FileName { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
}

public class TemplateParameter
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool Required { get; set; }
    public object? DefaultValue { get; set; }
    public List<object>? AllowedValues { get; set; }
    public int DisplayOrder { get; set; }
}

public class TemplateGuardrail
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? Property { get; set; }
    public string? Operator { get; set; }
    public string Value { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
}

public class ApprovalInfo
{
    public string Source { get; set; } = string.Empty;
    public string ApprovedBy { get; set; } = string.Empty;
    public DateTime ApprovedAt { get; set; }
    public string? Comments { get; set; }
}

public class CreateTemplateModel
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public string Category { get; set; } = string.Empty;
    public string Format { get; set; } = "Bicep";
    public string TemplateContent { get; set; } = string.Empty;
    public string Status { get; set; } = "Draft";
    public string? CreatedBy { get; set; }
    public bool RequiresApproval { get; set; } = true;
    public bool EnforceCompliance { get; set; } = true;
    public int? DefaultExpirationDays { get; set; }
    public List<string>? ComplianceFrameworks { get; set; }
    public List<string>? Keywords { get; set; }
    public List<string>? UseCases { get; set; }
    public string? AiSelectionHint { get; set; }
    public List<CreateParameterModel>? Parameters { get; set; }
    public List<CreateGuardrailModel>? Guardrails { get; set; }
}

public class CreateParameterModel
{
    public string Name { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? Type { get; set; }
    public bool Required { get; set; }
    public string? DefaultValue { get; set; }
    public List<string>? AllowedValues { get; set; }
    public int? MinValue { get; set; }
    public int? MaxValue { get; set; }
    public int DisplayOrder { get; set; }
}

public class CreateGuardrailModel
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Type { get; set; }
    public string? Property { get; set; }
    public string? Operator { get; set; }
    public string? Value { get; set; }
    public string? Action { get; set; }
    public string? ErrorMessage { get; set; }
}

public class GuardrailModel
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Type { get; set; }
    public string? Property { get; set; }
    public string? Operator { get; set; }
    public string? Value { get; set; }
    public string? Action { get; set; }
    public string? ErrorMessage { get; set; }
}

public class UpdateTemplateModel
{
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? Category { get; set; }
    public string? TemplateContent { get; set; }
    public List<string>? Keywords { get; set; }
    public List<string>? UseCases { get; set; }
    public string? AiSelectionHint { get; set; }
    public int? DefaultExpirationDays { get; set; }
    public string? Status { get; set; }
    public bool RequiresApproval { get; set; } = true;
    public bool EnforceCompliance { get; set; } = true;
    public List<string>? ComplianceFrameworks { get; set; }
    public string? UpdatedBy { get; set; }
    
    // Git Source Configuration
    public GitSourceModel? GitSource { get; set; }
    
    // Parameters
    public List<CreateParameterModel>? Parameters { get; set; }
    
    // Guardrails
    public List<GuardrailModel>? Guardrails { get; set; }
}

public class GitSourceModel
{
    public string? RepositoryUrl { get; set; }
    public string? Branch { get; set; }
    public string? Path { get; set; }
    public bool AutoSync { get; set; }
    public int SyncIntervalMinutes { get; set; } = 15;
}

public class ApprovalModel
{
    public string? Source { get; set; }
    public string? ApprovedBy { get; set; }
    public string? Comments { get; set; }
    public string? ExternalApprovalId { get; set; }
    public string? ExternalApprovalUrl { get; set; }
}

public class ValidateTemplateModel
{
    public string? Name { get; set; }
    public string? TemplateContent { get; set; }
    public string? Format { get; set; }
}

public class ValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

#endregion

#region Environment Models

public class EnvironmentListItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string TemplateId { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;
    public string SubscriptionId { get; set; } = string.Empty;
    public string ResourceGroup { get; set; } = string.Empty;
    public List<string>? ResourceGroups { get; set; }
    public string DeploymentScope { get; set; } = "resourceGroup";
    public string Location { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool HasDrift { get; set; }
    public int DriftCount { get; set; }
    public decimal EstimatedMonthlyCost { get; set; }
    public string? OwnerEmail { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

public class EnvironmentDetail
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string TemplateId { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;
    public string TemplateVersion { get; set; } = string.Empty;
    public string SubscriptionId { get; set; } = string.Empty;
    public string ResourceGroup { get; set; } = string.Empty;
    public List<string>? ResourceGroups { get; set; }
    public string DeploymentScope { get; set; } = "resourceGroup";
    public string Location { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? StatusMessage { get; set; }
    public bool HasDrift { get; set; }
    public int DriftCount { get; set; }
    public List<DriftItem>? DriftItems { get; set; }
    public decimal EstimatedMonthlyCost { get; set; }
    public string? OwnerEmail { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime? UpdatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool AutoDelete { get; set; }
    public Dictionary<string, string>? Tags { get; set; }
    public Dictionary<string, object>? ParameterValues { get; set; }
}

public class EnvironmentStatusSummary
{
    public int TotalEnvironments { get; set; }
    public int HealthyCount { get; set; }
    public int DegradedCount { get; set; }
    public int UnhealthyCount { get; set; }
    public int RunningEnvironments { get; set; }
    public int ProvisioningEnvironments { get; set; }
    public int FailedEnvironments { get; set; }
    public int EnvironmentsWithDrift { get; set; }
    public int ExpiringWithin7Days { get; set; }
    public decimal TotalEstimatedMonthlyCost { get; set; }
    public Dictionary<string, int>? ByTemplate { get; set; }
    public Dictionary<string, int>? ByStatus { get; set; }
}

public class CreateEnvironmentModel
{
    public string TemplateId { get; set; } = string.Empty;
    public string EnvironmentName { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string ResourceGroup { get; set; } = string.Empty;
    public string SubscriptionId { get; set; } = string.Empty;
    public string? Location { get; set; }
    public Dictionary<string, object>? Parameters { get; set; }
    public Dictionary<string, string>? Tags { get; set; }
    public string? OwnerEmail { get; set; }
    public string? RequestedBy { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool AutoDelete { get; set; }
}

public class CreateEnvironmentResult
{
    public bool Success { get; set; }
    public string? EnvironmentId { get; set; }
    public string? EnvironmentName { get; set; }
    public string? DeploymentId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Message { get; set; }
    public List<string>? Errors { get; set; }
    public EnvironmentDetail? Environment { get; set; }
}

public class RefreshDeploymentStatusResult
{
    public string EnvironmentId { get; set; } = string.Empty;
    public string EnvironmentName { get; set; } = string.Empty;
    public string DeploymentId { get; set; } = string.Empty;
    public string PreviousStatus { get; set; } = string.Empty;
    public string CurrentStatus { get; set; } = string.Empty;
    public string? StatusMessage { get; set; }
    public bool StatusChanged { get; set; }
    public string? Error { get; set; }
}

public class ScaleEnvironmentModel
{
    public int? NodeCount { get; set; }
    public int? ReplicaCount { get; set; }
    public string? Sku { get; set; }
    public string? Tier { get; set; }
    public Dictionary<string, object>? Parameters { get; set; }
    public string? ScaledBy { get; set; }
}

public class ScaleResult
{
    public bool Success { get; set; }
    public string? EnvironmentId { get; set; }
    public string? Message { get; set; }
    public List<string>? Errors { get; set; }
    public Dictionary<string, object>? OldValues { get; set; }
    public Dictionary<string, object>? NewValues { get; set; }
}

public class DriftDetectionResult
{
    public bool Success { get; set; }
    public string? EnvironmentId { get; set; }
    public string? EnvironmentName { get; set; }
    public bool HasDrift { get; set; }
    public int DriftCount { get; set; }
    public DateTime DetectedAt { get; set; }
    public List<DriftItem>? DriftItems { get; set; }
}

public class DriftItem
{
    public string Id { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
    public string ResourceName { get; set; } = string.Empty;
    public string PropertyPath { get; set; } = string.Empty;
    public string? ExpectedValue { get; set; }
    public string? ActualValue { get; set; }
    public string DriftType { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public bool CanAutoRemediate { get; set; }
}

public class RemediateDriftResult
{
    public bool Success { get; set; }
    public string? EnvironmentId { get; set; }
    public int ItemsRemediated { get; set; }
    public int ItemsFailed { get; set; }
    public int RemainingDriftCount { get; set; }
    public List<string>? Errors { get; set; }
}

public class DeleteResourcesResult
{
    public bool Success { get; set; }
    public string? EnvironmentId { get; set; }
    public string? Message { get; set; }
    public List<string>? DeletedResources { get; set; }
    public List<string>? FailedResources { get; set; }
    public List<string>? Errors { get; set; }
    public int TotalResourcesDeleted { get; set; }
    public int TotalResourcesFailed { get; set; }
}

public class SyncResourcesResult
{
    public string? EnvironmentId { get; set; }
    public string? EnvironmentName { get; set; }
    public int ResourcesFound { get; set; }
    public int ResourcesAdded { get; set; }
    public string? Message { get; set; }
    public string? Error { get; set; }
}

public class PurgeAllResult
{
    public int PurgedCount { get; set; }
}

public class EnvironmentHealth
{
    public string EnvironmentId { get; set; } = string.Empty;
    public string EnvironmentName { get; set; } = string.Empty;
    public string OverallHealth { get; set; } = string.Empty;
    public bool HasDrift { get; set; }
    public int DriftCount { get; set; }
    public decimal EstimatedMonthlyCost { get; set; }
    public DateTime LastChecked { get; set; }
    public List<string>? Issues { get; set; }
    public List<ResourceHealthItem>? ResourceHealth { get; set; }
}

public class ResourceHealthItem
{
    public string ResourceId { get; set; } = string.Empty;
    public string ResourceName { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public string Health { get; set; } = string.Empty;
    public string? Message { get; set; }
}

public class TemplateParameterDto
{
    public string Name { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string Type { get; set; } = string.Empty;
    public bool Required { get; set; }
    public string? DefaultValue { get; set; }
    public List<object>? AllowedValues { get; set; }
    public int? MinValue { get; set; }
    public int? MaxValue { get; set; }
    public int DisplayOrder { get; set; }
}

/// <summary>
/// Environment activity entry
/// </summary>
public class EnvironmentActivity
{
    public string Id { get; set; } = string.Empty;
    public string EnvironmentId { get; set; } = string.Empty;
    public string ActivityType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string? UserName { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
    public DateTime Timestamp { get; set; }
    public string Status { get; set; } = "Completed";
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Paged list of environment activities
/// </summary>
public class EnvironmentActivityList
{
    public List<EnvironmentActivity> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
    public bool HasMore => Skip + Items.Count < TotalCount;
}

#endregion

#region Compliance Models

public class ComplianceSummary
{
    public decimal OverallScore { get; set; }
    public int TotalControls { get; set; }
    public int CompliantControls { get; set; }
    public int NonCompliantControls { get; set; }
    public List<FrameworkScore> FrameworkScores { get; set; } = new();
    public List<EnvironmentComplianceStatus> EnvironmentStatuses { get; set; } = new();
    public List<ControlViolation> TopViolations { get; set; } = new();
}

public class FrameworkScore
{
    public string Framework { get; set; } = string.Empty;
    public decimal Score { get; set; }
    public int CompliantControls { get; set; }
    public int TotalControls { get; set; }
}

public class EnvironmentComplianceStatus
{
    public string EnvironmentId { get; set; } = string.Empty;
    public string EnvironmentName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal ComplianceScore { get; set; }
    public int CriticalViolations { get; set; }
    public int HighViolations { get; set; }
    public DateTime LastScannedAt { get; set; }
}

public class ControlViolation
{
    public string ControlId { get; set; } = string.Empty;
    public string ControlName { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int AffectedResourceCount { get; set; }
}

public class EnvironmentComplianceDetail
{
    public string EnvironmentId { get; set; } = string.Empty;
    public string EnvironmentName { get; set; } = string.Empty;
    public string SubscriptionId { get; set; } = string.Empty;
    public string SubscriptionName { get; set; } = string.Empty;
    public decimal OverallScore { get; set; }
    public DateTime LastScannedAt { get; set; }
    public List<FrameworkScore> FrameworkScores { get; set; } = new();
    public List<ControlComplianceDetail> Controls { get; set; } = new();
    public List<ResourceCompliance> Resources { get; set; } = new();
}

public class ControlComplianceDetail
{
    public string ControlId { get; set; } = string.Empty;
    public string ControlName { get; set; } = string.Empty;
    public string Framework { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> AffectedResources { get; set; } = new();
    public string? RemediationGuidance { get; set; }
}

public class ResourceCompliance
{
    public string ResourceId { get; set; } = string.Empty;
    public string ResourceName { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public bool IsCompliant { get; set; }
    public int ViolationCount { get; set; }
    public List<string> FailedControls { get; set; } = new();
}

#endregion

#region Deployed Resources

public class DeployedResource
{
    public string Id { get; set; } = string.Empty;
    public string EnvironmentId { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string? Sku { get; set; }
    public string ProvisioningState { get; set; } = string.Empty;
    public DateTime DeployedAt { get; set; }
    public string? AzurePortalUrl { get; set; }
}

public class DeployedResourceList
{
    public List<DeployedResource> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public string EnvironmentId { get; set; } = string.Empty;
    public string EnvironmentName { get; set; } = string.Empty;
}

#endregion

#region Developer Portal Models

public class IntegrationStatus
{
    public string Provider { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string? Organization { get; set; }
    public string? ServerUrl { get; set; }
}

public class ConnectionTestResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? Organization { get; set; }
    public string? UserName { get; set; }
    public int RepositoryCount { get; set; }
}

public class DevPortalRepository
{
    public string Id { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string DefaultBranch { get; set; } = "main";
    public string Url { get; set; } = string.Empty;
    public string CloneUrl { get; set; } = string.Empty;
    public string? Language { get; set; }
    public bool IsPrivate { get; set; }
    public int OpenIssueCount { get; set; }
    public int OpenPrCount { get; set; }
    public int Stars { get; set; }
    public DateTime? LastPushAt { get; set; }
    public DateTime? CreatedAt { get; set; }
}

#region Azure Resources

public class AzureResourceOverview
{
    public string Status { get; set; } = "not_configured";
    public string? Message { get; set; }
    public TenantInfo TenantInfo { get; set; } = new();
    public SubscriptionInfo SubscriptionInfo { get; set; } = new();
    public List<ManagementGroup> ManagementGroups { get; set; } = new();
    public List<PolicyAssignment> Policies { get; set; } = new();
    public List<ResourceGroup> ResourceGroups { get; set; } = new();
    public EntraIdInfo EntraIdInfo { get; set; } = new();
}

public class TenantInfo
{
    public string TenantId { get; set; } = "";
    public string CloudEnvironment { get; set; } = "";
    public string AuthMethod { get; set; } = "";
    public string? TenantDisplayName { get; set; }
}

public class SubscriptionInfo
{
    public string SubscriptionId { get; set; } = "";
    public string? DisplayName { get; set; }
    public string? State { get; set; }
}

public class ManagementGroup
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
}

public class PolicyAssignment
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string EnforcementMode { get; set; } = "";
}

public class ResourceGroup
{
    public string Name { get; set; } = "";
    public string Location { get; set; } = "";
    public string ProvisioningState { get; set; } = "";
}

public class EntraIdInfo
{
    public string? TenantDisplayName { get; set; }
    public int? UserCount { get; set; }
    public int? GroupCount { get; set; }
    public int? AppRegistrationCount { get; set; }
}

#endregion

public class DevPortalWorkItem
{
    public string Id { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string State { get; set; } = string.Empty;
    public string? AssignedTo { get; set; }
    public string? Priority { get; set; }
    public List<string> Labels { get; set; } = new();
    public string RepositoryName { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public int Number { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
}

public class DevPortalPipeline
{
    public string Id { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string RepositoryName { get; set; } = string.Empty;
    public string? Status { get; set; }
    public string? Conclusion { get; set; }
    public string? Branch { get; set; }
    public string Url { get; set; } = string.Empty;
    public DateTime? LastRunAt { get; set; }
    public int? DurationSeconds { get; set; }
    public string? TriggerEvent { get; set; }
}

public class DevPortalArtifact
{
    public string Id { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Version { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? FeedName { get; set; }
    public string? RepositoryName { get; set; }
    public long? SizeBytes { get; set; }
    public string? DownloadUrl { get; set; }
    public DateTime? PublishedAt { get; set; }
}

public class DevPortalSummary
{
    public int EnabledIntegrations { get; set; }
    public int TotalRepositories { get; set; }
    public int OpenWorkItems { get; set; }
    public int ActivePipelines { get; set; }
    public int RecentDeployments { get; set; }
    public List<IntegrationStatus> Integrations { get; set; } = new();
}

#endregion

#region Health Models

public class ResourceHealthResponse
{
    public string Status { get; set; } = "";
    public string? Message { get; set; }
    public string SubscriptionId { get; set; } = "";
    public int TotalResources { get; set; }
    public int AvailableCount { get; set; }
    public int DegradedCount { get; set; }
    public int UnavailableCount { get; set; }
    public int UnknownCount { get; set; }
    public List<ResourceHealthStatus> Resources { get; set; } = new();
}

public class ResourceHealthStatus
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

public class ServiceHealthResponse
{
    public string Status { get; set; } = "";
    public string? Message { get; set; }
    public string SubscriptionId { get; set; } = "";
    public int TotalAlerts { get; set; }
    public List<ServiceHealthAlert> Alerts { get; set; } = new();
}

public class ServiceHealthAlert
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

#endregion

#region Drift Detection (Real Azure)

public class DriftScanResponse
{
    public string Status { get; set; } = "ok";
    public string? Message { get; set; }
    public string? SubscriptionId { get; set; }
    public DateTime ScannedAt { get; set; }
    public int TotalResources { get; set; }
    public int DriftedResources { get; set; }
    public int CompliantResources { get; set; }
    public List<DriftedResource> DriftItems { get; set; } = new();
}

public class DriftedResource
{
    public string ResourceId { get; set; } = string.Empty;
    public string ResourceName { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public string ResourceGroup { get; set; } = string.Empty;
    public string DriftType { get; set; } = string.Empty;
    public string Severity { get; set; } = "Warning";
    public string PropertyPath { get; set; } = string.Empty;
    public string? ExpectedValue { get; set; }
    public string? ActualValue { get; set; }
    public string? PolicyName { get; set; }
    public string? PolicyDefinitionId { get; set; }
}

#endregion

#region Cost Management

public class CostSummaryResponse
{
    public string Status { get; set; } = "ok";
    public string? Message { get; set; }
    public string? SubscriptionId { get; set; }
    public DateTime QueryDate { get; set; }
    public decimal TotalCostLast30Days { get; set; }
    public string Currency { get; set; } = "USD";
    public List<DailyCost> DailyCosts { get; set; } = new();
    public List<ServiceCost> CostByService { get; set; } = new();
}

public class CostByResourceGroupResponse
{
    public string Status { get; set; } = "ok";
    public string? Message { get; set; }
    public string? SubscriptionId { get; set; }
    public List<ResourceGroupCost> ResourceGroups { get; set; } = new();
}

public class DailyCost
{
    public DateTime Date { get; set; }
    public decimal Cost { get; set; }
    public string Currency { get; set; } = "USD";
}

public class ServiceCost
{
    public string ServiceName { get; set; } = string.Empty;
    public decimal Cost { get; set; }
    public string Currency { get; set; } = "USD";
}

public class ResourceGroupCost
{
    public string ResourceGroupName { get; set; } = string.Empty;
    public decimal Cost { get; set; }
    public string Currency { get; set; } = "USD";
    public int ResourceCount { get; set; }
}

#endregion

#region Migration & Modernization Models

public class MigrationAssessmentRequest
{
    public string ApplicationName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? FrameworkVersion { get; set; }
    public string? ProjectFileContent { get; set; }
    public string? HostingEnvironment { get; set; }
    public string? DatabaseType { get; set; }
    public string? TargetService { get; set; }
}

public class MigrationAssessment
{
    public string Id { get; set; } = string.Empty;
    public string ApplicationName { get; set; } = string.Empty;
    public string Status { get; set; } = "ok";
    public string? Message { get; set; }
    public DateTime AssessedAt { get; set; }
    public int OverallReadinessScore { get; set; }
    public int FrameworkCompatibilityScore { get; set; }
    public int DependencyHealthScore { get; set; }
    public int DatabaseCouplingScore { get; set; }
    public int SecurityPostureScore { get; set; }
    public string RecommendedStrategy { get; set; } = string.Empty;
    public string RecommendedAzureService { get; set; } = string.Empty;
    public List<string> Blockers { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();
    public List<MigrationPhase> MigrationPhases { get; set; } = new();
}

public class MigrationPhase
{
    public int Phase { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string EstimatedEffort { get; set; } = string.Empty;
    public List<string> Tasks { get; set; } = new();
}

public class MigrationDashboard
{
    public string Status { get; set; } = "ok";
    public string? Message { get; set; }
    public int TotalAssessments { get; set; }
    public int ReadyToMigrate { get; set; }
    public int NeedsWork { get; set; }
    public int Blocked { get; set; }
    public double AverageReadinessScore { get; set; }
    public List<MigrationAssessment> RecentAssessments { get; set; } = new();
}

public class AzureTargetRecommendation
{
    public string TargetService { get; set; } = string.Empty;
    public string Rationale { get; set; } = string.Empty;
    public int CompatibilityScore { get; set; }
    public decimal EstimatedMonthlyCost { get; set; }
    public List<string> Pros { get; set; } = new();
    public List<string> Cons { get; set; } = new();
    public bool SupportsGovCloud { get; set; }
}

public class TargetRecommendationsResponse
{
    public string Status { get; set; } = "ok";
    public string? Message { get; set; }
    public string ApplicationName { get; set; } = string.Empty;
    public List<AzureTargetRecommendation> Recommendations { get; set; } = new();
}

#endregion
