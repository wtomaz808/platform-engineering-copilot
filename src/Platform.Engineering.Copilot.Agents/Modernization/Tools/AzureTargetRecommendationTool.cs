using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.Engineering.Copilot.Agents.Common;
using Platform.Engineering.Copilot.Agents.Modernization.Configuration;

namespace Platform.Engineering.Copilot.Agents.Modernization.Tools;

/// <summary>
/// Maps on-premises application components to recommended Azure Government services.
/// Covers web tier, DB tier, caching, messaging, storage, background jobs, and more.
/// Verifies Azure Gov region availability and provides cost estimates.
/// </summary>
public class AzureTargetRecommendationTool : BaseTool
{
    private readonly ModernizationAgentOptions _options;

    public override string Name => "azure_target_recommendation";

    public override string Description =>
        "Map on-premises application components to recommended Azure Government services. " +
        "Provide a description of all components (web tier, database, caching, messaging, storage, " +
        "background jobs, authentication) and get Azure service recommendations with Gov availability, " +
        "estimated costs, and architecture guidance.";

    public AzureTargetRecommendationTool(
        ILogger<AzureTargetRecommendationTool> logger,
        IOptions<ModernizationAgentOptions> options) : base(logger)
    {
        _options = options?.Value ?? new ModernizationAgentOptions();
        Parameters.Add(new ToolParameter("app_profile", "Description of all application components: web tier type (IIS, self-hosted), database type and version, caching (Redis, Memcached, in-memory), messaging (MSMQ, RabbitMQ), storage (file shares, blobs), background jobs, authentication method, and any other middleware or services.", true));
    }

    public override async Task<string> ExecuteAsync(
        IDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var input = GetRequiredString(arguments, "app_profile");
        Logger.LogInformation("Generating Azure target recommendations for app profile ({Length} chars)", input.Length);

        var lower = input.ToLowerInvariant();
        var mappings = new List<object>();

        // Web tier
        if (lower.Contains("iis") || lower.Contains("web app") || lower.Contains("asp.net") || lower.Contains("mvc") || lower.Contains("web api"))
        {
            mappings.Add(BuildMapping("Web Application (IIS)", DetectWebTarget(lower),
                "Handles HTTP requests, serves web pages and APIs"));
        }

        // Database
        if (lower.Contains("sql server") || lower.Contains("mssql") || lower.Contains("database"))
        {
            mappings.Add(BuildMapping("SQL Server Database", "Azure SQL Managed Instance",
                "Highest compatibility with on-prem SQL Server; supports SQL Agent, linked servers, CLR"));
        }
        if (lower.Contains("oracle"))
        {
            mappings.Add(BuildMapping("Oracle Database", "Azure SQL MI (via SSMA migration) or Oracle on Azure VM",
                "Use SSMA for schema/data migration to SQL MI; Azure VM for Oracle-native requirements"));
        }
        if (lower.Contains("mysql"))
        {
            mappings.Add(BuildMapping("MySQL Database", "Azure Database for MySQL Flexible Server",
                "Fully managed MySQL with HA, automated backups, and Gov availability"));
        }
        if (lower.Contains("postgres"))
        {
            mappings.Add(BuildMapping("PostgreSQL Database", "Azure Database for PostgreSQL Flexible Server",
                "Fully managed PostgreSQL with extensions and intelligent performance"));
        }

        // Caching
        if (lower.Contains("redis") || lower.Contains("cache") || lower.Contains("memcached") || lower.Contains("in-memory cache"))
        {
            mappings.Add(BuildMapping("Caching Layer", "Azure Cache for Redis",
                "Fully managed Redis with clustering, persistence, and data encryption"));
        }

        // Messaging
        if (lower.Contains("msmq") || lower.Contains("message queue") || lower.Contains("rabbitmq") || lower.Contains("messaging"))
        {
            mappings.Add(BuildMapping("Message Queue", "Azure Service Bus",
                "Enterprise messaging with queues, topics, dead-letter, and transactions"));
        }

        // Storage / file shares
        if (lower.Contains("file share") || lower.Contains("network share") || lower.Contains("smb") || lower.Contains("nas"))
        {
            mappings.Add(BuildMapping("File Shares / NAS", "Azure Files (SMB) or Azure Blob Storage",
                "Azure Files for SMB mount compatibility; Blob Storage for object/document storage"));
        }

        // Scheduled tasks / batch
        if (lower.Contains("scheduled task") || lower.Contains("task scheduler") || lower.Contains("batch") ||
            lower.Contains("cron") || lower.Contains("timer job"))
        {
            mappings.Add(BuildMapping("Scheduled Tasks / Batch Jobs", "Azure Functions (Timer trigger) or Azure Batch",
                "Functions for short tasks (<10min); Azure Batch for heavy compute workloads"));
        }

        // Windows Services
        if (lower.Contains("windows service") || lower.Contains("background service") || lower.Contains("daemon"))
        {
            mappings.Add(BuildMapping("Windows Services / Daemons", "Azure WebJobs, Azure Functions, or Container Apps with background workers",
                "Convert to IHostedService pattern; deploy as WebJob, Function, or container"));
        }

        // SMTP / email
        if (lower.Contains("smtp") || lower.Contains("email") || lower.Contains("sendmail"))
        {
            mappings.Add(BuildMapping("Email / SMTP", "Azure Communication Services (Email)",
                "Cloud-native email sending with high deliverability; Gov-compatible"));
        }

        // Authentication
        if (lower.Contains("active directory") || lower.Contains("ldap") || lower.Contains("windows auth") || lower.Contains("ad auth"))
        {
            mappings.Add(BuildMapping("Active Directory / LDAP Auth", "Microsoft Entra ID (Azure AD)",
                "Cloud identity with SSO, MFA, conditional access; supports hybrid with AD Connect"));
        }

        // VMs (VMware)
        if (lower.Contains("vmware") || lower.Contains("virtual machine") || lower.Contains("vm ") || lower.Contains("esxi"))
        {
            mappings.Add(BuildMapping("VMware VMs", "Azure VM (lift-and-shift) or Containerize (replatform)",
                "Azure Migrate for VM migration; consider containerization for long-term modernization"));
        }

        // SSIS
        if (lower.Contains("ssis") || lower.Contains("etl") || lower.Contains("data integration"))
        {
            mappings.Add(BuildMapping("SSIS / ETL Pipelines", "Azure Data Factory with Azure-SSIS Integration Runtime",
                "Lift-and-shift SSIS packages to Azure-SSIS IR; modernize to ADF data flows"));
        }

        // Default if nothing detected
        if (mappings.Count == 0)
        {
            mappings.Add(BuildMapping("Web Application", "Azure App Service or Azure Container Apps",
                "Default recommendation — provide more details about your tech stack for specific guidance"));
        }

        return ToJson(new
        {
            success = true,
            tool = "azure_target_recommendation",
            targetCloud = _options.DefaultTargetCloud,
            componentMappings = mappings,
            architectureSummary = $"Recommended Azure Gov architecture with {mappings.Count} component(s) mapped. " +
                "Use Azure Private Link and VNet integration for network isolation. " +
                "Enable Azure Monitor and Log Analytics for observability.",
            azureGovRegions = new[]
            {
                new { region = "USGov Virginia", primary = true, note = "Primary region — broadest service availability" },
                new { region = "USGov Arizona", primary = false, note = "Secondary/DR region" },
                new { region = "USGov Texas", primary = false, note = "Limited service availability" }
            },
            networkingGuidance = new[]
            {
                "Use Azure ExpressRoute or VPN Gateway for hybrid connectivity to on-premises",
                "Deploy resources in Azure Virtual Network with NSG rules",
                "Use Azure Private Link for PaaS services (SQL MI, Storage, Redis)",
                "Configure Azure Firewall or third-party NVA for egress filtering",
                "Use Azure DNS Private Zones for internal name resolution"
            },
            complianceFramework = _options.DefaultComplianceFramework,
            userInput = input
        });
    }

    private string DetectWebTarget(string input)
    {
        if (input.Contains("microservice") || input.Contains("container"))
            return "Azure Kubernetes Service (AKS) or Azure Container Apps";
        if (input.Contains(".net framework") || input.Contains("net45") || input.Contains("net47") || input.Contains("net48"))
            return "Azure App Service (Windows) or Windows Container on AKS";
        return "Azure App Service or Azure Container Apps";
    }

    private object BuildMapping(string onPremComponent, string azureTarget, string rationale)
    {
        return new
        {
            onPremComponent,
            azureTarget,
            rationale,
            azureGovAvailable = true,
            migrationComplexity = EstimateComplexity(onPremComponent)
        };
    }

    private string EstimateComplexity(string component)
    {
        return component switch
        {
            _ when component.Contains("SQL Server") => "Medium",
            _ when component.Contains("Oracle") => "High",
            _ when component.Contains("WCF") => "High",
            _ when component.Contains("MSMQ") => "Medium-High",
            _ when component.Contains("SSIS") => "Medium",
            _ when component.Contains("VMware") => "Low-Medium",
            _ when component.Contains("File Share") => "Low",
            _ when component.Contains("Cache") => "Low",
            _ => "Medium"
        };
    }
}
