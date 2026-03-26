using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.Engineering.Copilot.Agents.Common;
using Platform.Engineering.Copilot.Agents.Modernization.Configuration;

namespace Platform.Engineering.Copilot.Agents.Modernization.Tools;

/// <summary>
/// Evaluates migration compliance readiness against NIST 800-53 controls and FedRAMP requirements.
/// Maps migration decisions against security controls and generates pre/post migration checklists.
/// </summary>
public class ComplianceReadinessTool : BaseTool
{
    private readonly ModernizationAgentOptions _options;

    public override string Name => "compliance_readiness";

    public override string Description =>
        "Evaluate migration compliance readiness against NIST 800-53 and FedRAMP requirements for Azure Government. " +
        "Maps migration decisions against security controls (SC-8, SC-28, AC-2, AU-2, IA-2, CM-6). " +
        "Generates pre-migration and post-migration compliance checklists. " +
        "Provide the migration context, target services, data classification, and compliance requirements.";

    public ComplianceReadinessTool(
        ILogger<ComplianceReadinessTool> logger,
        IOptions<ModernizationAgentOptions> options) : base(logger)
    {
        _options = options?.Value ?? new ModernizationAgentOptions();
        Parameters.Add(new ToolParameter("migration_context", "Description of the migration: source environment, target Azure services, data types handled (PII, PHI, CUI), compliance requirements (FedRAMP High/Moderate, NIST 800-53, IL4/IL5), and any existing compliance certifications.", true));
    }

    public override async Task<string> ExecuteAsync(
        IDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var input = GetRequiredString(arguments, "migration_context");
        Logger.LogInformation("Running compliance readiness assessment ({Length} chars)", input.Length);

        var lower = input.ToLowerInvariant();

        // Detect compliance framework
        var framework = _options.DefaultComplianceFramework;
        if (lower.Contains("fedramp moderate")) framework = "FedRAMP-Moderate";
        if (lower.Contains("fedramp high")) framework = "FedRAMP-High";
        if (lower.Contains("il5") || lower.Contains("impact level 5")) framework = "DoD IL5";
        if (lower.Contains("il4") || lower.Contains("impact level 4")) framework = "DoD IL4";

        // Build NIST 800-53 control assessment
        var controls = BuildControlAssessment(lower);

        // Pre-migration checklist
        var preMigration = new[]
        {
            new { item = "Data Classification", description = "Classify all data handled by the application (PII, PHI, CUI, FOUO)", status = DetectStatus(lower, "classified", "data classification") },
            new { item = "System Security Plan (SSP)", description = "Document current security controls and planned post-migration state", status = "Required" },
            new { item = "Boundary Diagram", description = "Update authorization boundary to include Azure Gov resources", status = "Required" },
            new { item = "Encryption Inventory", description = "Document all encryption at rest and in transit mechanisms", status = DetectStatus(lower, "encrypted", "encryption") },
            new { item = "Access Control Inventory", description = "Document all user/service accounts and their access levels", status = "Required" },
            new { item = "Audit Log Configuration", description = "Plan Azure Monitor, Log Analytics, and diagnostic settings", status = "Required" },
            new { item = "Penetration Test Plan", description = "Schedule pre-migration pen test and post-migration retest", status = "Recommended" },
            new { item = "Incident Response Update", description = "Update IR plan with Azure-specific procedures and contacts", status = "Required" },
            new { item = "Contingency Plan", description = "Document rollback procedures and service continuity plan", status = "Required" },
            new { item = "POA&M Review", description = "Review Plan of Action & Milestones for migration-impacted items", status = "Required" }
        };

        // Post-migration checklist
        var postMigration = new[]
        {
            new { item = "Azure Policy Assignment", description = "Apply FedRAMP/NIST policy initiatives to all resource groups", priority = "Immediate" },
            new { item = "Microsoft Defender for Cloud", description = "Enable Defender plans for all resource types; set to Enhanced", priority = "Immediate" },
            new { item = "Diagnostic Settings", description = "Configure all resources to send logs to Log Analytics workspace", priority = "Immediate" },
            new { item = "Key Vault Integration", description = "Migrate all secrets/certs to Azure Key Vault; enable soft-delete and purge protection", priority = "Day 1" },
            new { item = "Network Security Validation", description = "Verify NSG rules, Private Link, and firewall configurations", priority = "Day 1" },
            new { item = "Identity Verification", description = "Confirm Managed Identity assignments and RBAC least-privilege", priority = "Day 1" },
            new { item = "Encryption Validation", description = "Verify TDE, TLS 1.2+, and Key Vault-managed keys for all data stores", priority = "Week 1" },
            new { item = "Compliance Scan", description = "Run Azure Policy compliance scan and remediate non-compliant resources", priority = "Week 1" },
            new { item = "Penetration Test", description = "Execute post-migration penetration test against Azure-hosted services", priority = "Month 1" },
            new { item = "Continuous Monitoring", description = "Configure Azure Sentinel, alert rules, and automated response playbooks", priority = "Month 1" },
            new { item = "ATO Update", description = "Update Authority to Operate documentation with Azure Gov deployment details", priority = "Month 1" }
        };

        return ToJson(new
        {
            success = true,
            tool = "compliance_readiness",
            complianceFramework = framework,
            targetCloud = _options.DefaultTargetCloud,
            nistControls = controls,
            preMigrationChecklist = preMigration,
            postMigrationChecklist = postMigration,
            azureGovCompliance = new
            {
                fedrampHigh = "Azure Government is FedRAMP High authorized",
                dodIl = "Azure Government supports DoD IL2, IL4, and IL5",
                nist80053 = "Azure Gov provides NIST 800-53 control implementations",
                azurePolicy = "Built-in policy initiatives for FedRAMP High, NIST 800-53 r5, DoD IL5",
                complianceManager = "Microsoft Purview Compliance Manager for ongoing compliance tracking"
            },
            handoffNote = "For detailed compliance assessment of existing Azure resources, use the Compliance Agent " +
                "(type 'compliance assessment' or 'NIST 800-53 audit') which can scan live Azure resources.",
            userInput = input
        });
    }

    private List<object> BuildControlAssessment(string input)
    {
        return new List<object>
        {
            new {
                controlId = "SC-8",
                controlName = "Transmission Confidentiality and Integrity",
                requirement = "Protect data in transit with encryption",
                migrationImpact = "All Azure connections must use TLS 1.2+",
                azureImplementation = "App Service enforces HTTPS; Azure SQL/Redis use encrypted connections; VPN/ExpressRoute for hybrid",
                status = input.Contains("tls") || input.Contains("https") || input.Contains("encrypted") ? "Addressed" : "Needs Review"
            },
            new {
                controlId = "SC-28",
                controlName = "Protection of Information at Rest",
                requirement = "Encrypt stored data",
                migrationImpact = "All Azure storage and databases must have encryption at rest enabled",
                azureImplementation = "Azure Storage SSE, SQL TDE, Azure Disk Encryption — all enabled by default in Azure Gov",
                status = input.Contains("encryption at rest") || input.Contains("tde") ? "Addressed" : "Needs Review"
            },
            new {
                controlId = "AC-2",
                controlName = "Account Management",
                requirement = "Manage and monitor user/service accounts",
                migrationImpact = "Migrate identity to Entra ID / Managed Identity; eliminate shared accounts",
                azureImplementation = "Entra ID for user identity, Managed Identity for services, Azure PIM for privileged access",
                status = input.Contains("managed identity") || input.Contains("entra") ? "Addressed" : "Needs Review"
            },
            new {
                controlId = "AU-2",
                controlName = "Event Logging",
                requirement = "Audit security-relevant events",
                migrationImpact = "Configure Azure Monitor, Log Analytics, and Activity Logs for all resources",
                azureImplementation = "Azure Monitor diagnostic settings, Log Analytics workspace, Azure Activity Log, Azure Sentinel",
                status = input.Contains("logging") || input.Contains("audit") || input.Contains("monitor") ? "Addressed" : "Needs Review"
            },
            new {
                controlId = "IA-2",
                controlName = "Identification and Authentication",
                requirement = "Uniquely identify and authenticate users",
                migrationImpact = "Enforce MFA and strong authentication in cloud environment",
                azureImplementation = "Entra ID with MFA, Conditional Access policies, passwordless authentication",
                status = input.Contains("mfa") || input.Contains("multi-factor") ? "Addressed" : "Needs Review"
            },
            new {
                controlId = "CM-6",
                controlName = "Configuration Settings",
                requirement = "Establish and enforce secure configurations",
                migrationImpact = "Apply Azure Policy baselines and security hardening",
                azureImplementation = "Azure Policy (FedRAMP High initiative), Microsoft Defender for Cloud secure score, Blueprints",
                status = input.Contains("policy") || input.Contains("hardening") ? "Addressed" : "Needs Review"
            }
        };
    }

    private string DetectStatus(string input, params string[] keywords)
    {
        return keywords.Any(k => input.Contains(k)) ? "In Progress" : "Required";
    }
}
