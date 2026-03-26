using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.Engineering.Copilot.Agents.Common;
using Platform.Engineering.Copilot.Agents.Modernization.Configuration;

namespace Platform.Engineering.Copilot.Agents.Modernization.Tools;

/// <summary>
/// Generates a comprehensive phased migration plan covering Assessment → Preparation → Migration → Optimization.
/// Includes timeline estimates, risk register, rollback procedures, Azure Gov–specific considerations,
/// and incorporates batch/scheduled task findings.
/// </summary>
public class MigrationPlanGeneratorTool : BaseTool
{
    private readonly ModernizationAgentOptions _options;

    public override string Name => "migration_plan_generator";

    public override string Description =>
        "Generate a comprehensive, phased migration plan for moving on-premises .NET applications to Azure Government. " +
        "Produces a 4-phase plan (Assess → Prepare → Migrate → Optimize) with timeline, risk register, " +
        "rollback procedures, resource requirements, and Azure Gov considerations. " +
        "Provide the assessment summary from other tools or describe the application portfolio.";

    public MigrationPlanGeneratorTool(
        ILogger<MigrationPlanGeneratorTool> logger,
        IOptions<ModernizationAgentOptions> options) : base(logger)
    {
        _options = options?.Value ?? new ModernizationAgentOptions();
        Parameters.Add(new ToolParameter("assessment_summary", "Summary of the application portfolio, assessment findings from other modernization tools, migration strategy (rehost/replatform/refactor), number of applications, database types, compliance requirements, and team size.", true));
    }

    public override async Task<string> ExecuteAsync(
        IDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var input = GetRequiredString(arguments, "assessment_summary");
        Logger.LogInformation("Generating migration plan from assessment ({Length} chars)", input.Length);

        var lower = input.ToLowerInvariant();

        // Detect scope complexity
        var complexity = DetectComplexity(lower);

        return ToJson(new
        {
            success = true,
            tool = "migration_plan_generator",
            targetCloud = _options.DefaultTargetCloud,
            complianceFramework = _options.DefaultComplianceFramework,
            complexity,
            migrationPlan = new
            {
                phases = new[]
                {
                    new
                    {
                        phase = "Phase 1: Assess & Discover",
                        duration = complexity == "High" ? "4-6 weeks" : complexity == "Medium" ? "2-4 weeks" : "1-2 weeks",
                        objectives = new[]
                        {
                            "Complete application portfolio inventory and dependency mapping",
                            "Run automated assessments (App Assessment, DB Migration, Security Scan tools)",
                            "Classify applications by migration strategy (Rehost/Replatform/Refactor)",
                            "Conduct compliance gap analysis against " + _options.DefaultComplianceFramework,
                            "Identify batch processes and scheduled tasks for migration planning",
                            "Estimate Azure costs and select service tiers"
                        },
                        deliverables = new[] { "Application inventory", "Migration wave plan", "Compliance gap report", "Cost estimate", "Risk register" },
                        azureTools = new[] { "Azure Migrate", "Data Migration Assistant", "Azure TCO Calculator", "Modernization Agent tools" }
                    },
                    new
                    {
                        phase = "Phase 2: Prepare & Design",
                        duration = complexity == "High" ? "4-8 weeks" : complexity == "Medium" ? "3-5 weeks" : "2-3 weeks",
                        objectives = new[]
                        {
                            "Set up Azure Gov landing zone (subscriptions, VNets, NSGs, policies)",
                            "Configure hybrid connectivity (ExpressRoute/VPN to on-premises)",
                            "Provision target Azure services (App Service, SQL MI, AKS, etc.)",
                            "Implement identity integration (Entra ID Connect, Managed Identities)",
                            "Apply Azure Policy initiatives (" + _options.DefaultComplianceFramework + ")",
                            "Set up CI/CD pipelines for target environment",
                            "Configure Azure Monitor, Log Analytics, and alerting",
                            "Create rollback runbooks and contingency procedures"
                        },
                        deliverables = new[] { "Landing zone deployed", "Network connectivity verified", "CI/CD pipelines", "Monitoring configured", "Rollback runbooks" },
                        azureTools = new[] { "Azure Landing Zone Accelerator", "Azure Policy", "Azure DevOps / GitHub Actions", "Azure Monitor" }
                    },
                    new
                    {
                        phase = "Phase 3: Migrate & Validate",
                        duration = complexity == "High" ? "8-16 weeks" : complexity == "Medium" ? "4-8 weeks" : "2-4 weeks",
                        objectives = new[]
                        {
                            "Execute migration waves (start with low-risk applications)",
                            "Migrate databases using DMS/bacpac with validation checkpoints",
                            "Deploy containerized applications to AKS/Container Apps",
                            "Migrate batch processes to Azure Functions/Batch/Data Factory",
                            "Perform integration testing in Azure Gov environment",
                            "Execute security validation and penetration testing",
                            "Conduct user acceptance testing (UAT)",
                            "Cutover DNS and traffic with rollback capability"
                        },
                        deliverables = new[] { "Applications deployed to Azure Gov", "Databases migrated", "Batch processes migrated", "UAT sign-off", "Performance baselines" },
                        azureTools = new[] { "Azure Database Migration Service", "Azure Container Registry", "Azure DevOps", "Azure Load Testing" }
                    },
                    new
                    {
                        phase = "Phase 4: Optimize & Govern",
                        duration = "Ongoing (first 4-8 weeks intensive)",
                        objectives = new[]
                        {
                            "Performance tuning and right-sizing of Azure resources",
                            "Cost optimization — Reserved Instances, auto-scale policies, unused resource cleanup",
                            "Complete ATO (Authority to Operate) update with Azure Gov details",
                            "Implement continuous compliance monitoring with Azure Policy",
                            "Set up Azure Sentinel for security monitoring and incident response",
                            "Decommission on-premises infrastructure (after validation period)",
                            "Knowledge transfer and team training on Azure operations",
                            "Establish operational runbooks for day-2 operations"
                        },
                        deliverables = new[] { "Optimized resource configurations", "Updated ATO/SSP", "Operational runbooks", "On-prem decommission plan", "Team training complete" },
                        azureTools = new[] { "Azure Advisor", "Azure Cost Management", "Microsoft Defender for Cloud", "Azure Sentinel" }
                    }
                },
                migrationWaveStrategy = new
                {
                    description = "Group applications into migration waves based on dependencies, risk, and strategy",
                    waves = new[]
                    {
                        new { wave = "Wave 0 — Foundation", scope = "Landing zone, networking, identity, monitoring infrastructure", risk = "Low" },
                        new { wave = "Wave 1 — Quick wins", scope = "Simple rehost apps with minimal dependencies (static sites, APIs)", risk = "Low" },
                        new { wave = "Wave 2 — Core apps", scope = "Main business applications requiring replatform (containerization)", risk = "Medium" },
                        new { wave = "Wave 3 — Complex apps", scope = "Legacy apps requiring refactoring (WCF, .NET Framework, batch processes)", risk = "High" },
                        new { wave = "Wave 4 — Databases", scope = "Database migrations (can parallel with app waves if using DMS online mode)", risk = "Medium-High" }
                    }
                },
                riskRegister = new[]
                {
                    new { risk = "Data loss during migration", likelihood = "Low", impact = "Critical", mitigation = "Use DMS online mode with continuous sync; maintain source as fallback" },
                    new { risk = "Extended downtime", likelihood = "Medium", impact = "High", mitigation = "Blue-green deployment; DNS-based cutover with instant rollback" },
                    new { risk = "Performance degradation", likelihood = "Medium", impact = "Medium", mitigation = "Load test in Azure Gov before cutover; right-size with Azure Advisor" },
                    new { risk = "Compliance gap discovered post-migration", likelihood = "Low", impact = "High", mitigation = "Pre-migration compliance scan; Azure Policy continuous enforcement" },
                    new { risk = "Network connectivity issues", likelihood = "Medium", impact = "High", mitigation = "Redundant VPN/ExpressRoute; test failover procedures" },
                    new { risk = "Team skill gaps", likelihood = "High", impact = "Medium", mitigation = "Azure training (AZ-900, AZ-104, AZ-305); pair programming with Azure experts" },
                    new { risk = "Cost overrun", likelihood = "Medium", impact = "Medium", mitigation = "Azure Cost Management alerts; right-sizing review at each phase gate" }
                },
                rollbackProcedures = new
                {
                    strategy = "Maintain parallel operation capability for 2-4 weeks after cutover",
                    steps = new[]
                    {
                        "Keep on-premises systems running in read-only/standby mode during validation period",
                        "Maintain DNS records with low TTL for instant traffic redirection",
                        "Database: Keep DMS replication active for reverse sync capability",
                        "Document rollback triggers: SLA violation, data integrity issue, security incident",
                        "Rollback execution: Revert DNS, redirect traffic, verify on-prem systems operational",
                        "Post-rollback: Root cause analysis and re-plan migration wave"
                    }
                }
            },
            userInput = input
        });
    }

    private string DetectComplexity(string input)
    {
        var score = 0;
        if (input.Contains("wcf") || input.Contains("msmq") || input.Contains("com ")) score += 2;
        if (input.Contains("oracle") || input.Contains("multiple database")) score += 2;
        if (input.Contains("ssis") || input.Contains("linked server")) score += 1;
        if (input.Contains("batch") || input.Contains("scheduled")) score += 1;
        if (input.Contains(".net framework") || input.Contains("net45") || input.Contains("legacy")) score += 1;
        if (input.Contains("multiple app") || input.Contains("portfolio") || input.Contains("10+")) score += 2;
        if (input.Contains("il5") || input.Contains("fedramp high")) score += 1;

        return score switch
        {
            >= 5 => "High",
            >= 3 => "Medium",
            _ => "Low"
        };
    }
}
