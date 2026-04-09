using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.Engineering.Copilot.Agents.Common;
using Platform.Engineering.Copilot.Agents.Modernization.Configuration;
using Platform.Engineering.Copilot.Agents.Modernization.Tools;
using Platform.Engineering.Copilot.State.Abstractions;

namespace Platform.Engineering.Copilot.Agents.Modernization.Agents;

/// <summary>
/// Specialized agent for assessing and planning modernization/migration of on-premises
/// .NET applications to Azure Government. Supports application assessment, database migration,
/// containerization readiness, Azure target mapping, security scanning, compliance readiness,
/// batch process migration, and comprehensive migration plan generation.
/// </summary>
public class ModernizationAgent : BaseAgent
{
    private readonly ModernizationAgentOptions _options;

    public override string AgentId => "modernization";
    public override string AgentName => "Modernization Agent";

    public override string Description =>
        "Specialized agent for modernizing and migrating on-premises .NET applications to Azure Government. " +
        "Assesses application readiness, database migration complexity, containerization feasibility, " +
        "security posture, compliance readiness (FedRAMP/NIST), and generates phased migration plans. " +
        "Supports .NET Framework 3.5–4.8 to .NET 8/9, IIS, WCF, MSMQ, SQL Server, batch processes, and more.";

    protected override float Temperature => (float)_options.Temperature;
    protected override int MaxTokens => _options.MaxTokens;

    public ModernizationAgent(
        IChatClient chatClient,
        ILogger<ModernizationAgent> logger,
        IOptions<ModernizationAgentOptions> options,
        AppAssessmentTool appAssessmentTool,
        DatabaseMigrationTool databaseMigrationTool,
        ContainerizationAssessmentTool containerizationTool,
        AzureTargetRecommendationTool azureTargetTool,
        SecurityScanTool securityScanTool,
        ComplianceReadinessTool complianceReadinessTool,
        MigrationPlanGeneratorTool migrationPlanTool,
        BatchProcessMigrationTool batchProcessTool,
        ScanGitHubRepoTool scanGitHubRepoTool,
        ScanADORepoTool scanAdoRepoTool,
        IAgentStateManager? agentStateManager = null,
        ISharedMemory? sharedMemory = null)
        : base(chatClient, logger, agentStateManager, sharedMemory)
    {
        _options = options?.Value ?? new ModernizationAgentOptions();

        // Register all modernization tools
        RegisterTool(scanGitHubRepoTool);
        RegisterTool(scanAdoRepoTool);
        RegisterTool(appAssessmentTool);
        RegisterTool(databaseMigrationTool);
        RegisterTool(containerizationTool);
        RegisterTool(azureTargetTool);
        RegisterTool(securityScanTool);
        RegisterTool(complianceReadinessTool);
        RegisterTool(migrationPlanTool);
        RegisterTool(batchProcessTool);

        Logger.LogInformation(
            "ModernizationAgent initialized with {ToolCount} tools. Target: {TargetCloud}, Compliance: {Framework}",
            RegisteredTools.Count, _options.DefaultTargetCloud, _options.DefaultComplianceFramework);
    }

    protected override string GetSystemPrompt()
    {
        var variables = new Dictionary<string, string>
        {
            ["DefaultTargetCloud"] = _options.DefaultTargetCloud,
            ["DefaultComplianceFramework"] = _options.DefaultComplianceFramework
        };

        var template = SystemPromptLoader.LoadFromType<ModernizationAgent>("ModernizationAgent.prompt.txt") ?? "";
        return SystemPromptLoader.ApplyVariables(template, variables);
    }

    public override async Task<AgentResponse> ProcessAsync(
        AgentConversationContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            Logger.LogInformation("ModernizationAgent processing request for conversation: {ConversationId}",
                context.ConversationId);

            var userMessage = context.MessageHistory.LastOrDefault(m => m.IsUser)?.Content ?? "";
            var intent = AnalyzeModernizationIntent(userMessage);
            Logger.LogDebug("Detected modernization intent: {Intent}", intent);

            // Use base agent processing with tool execution
            var response = await base.ProcessAsync(context, cancellationToken);

            return response;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in ModernizationAgent.ProcessAsync");
            return new AgentResponse
            {
                Success = false,
                AgentId = AgentId,
                Content = $"An error occurred during modernization processing: {ex.Message}"
            };
        }
    }

    private string AnalyzeModernizationIntent(string message)
    {
        var lower = message.ToLowerInvariant();

        // App assessment intents
        if (lower.Contains("assess") && (lower.Contains("app") || lower.Contains("application") || lower.Contains("project")))
            return "app_assessment";
        if (lower.Contains(".csproj") || lower.Contains("web.config") || lower.Contains("packages.config"))
            return "app_assessment";

        // Database intents
        if (lower.Contains("database") || lower.Contains("sql server") || lower.Contains("connection string"))
            return "database_migration";

        // Containerization intents
        if (lower.Contains("container") || lower.Contains("docker") || lower.Contains("aks") || lower.Contains("acr"))
            return "containerization_assessment";

        // Azure target mapping
        if (lower.Contains("azure service") || lower.Contains("what service") || lower.Contains("architecture") ||
            lower.Contains("target") || lower.Contains("recommend"))
            return "azure_target_recommendation";

        // Security
        if (lower.Contains("security") || lower.Contains("vulnerab") || lower.Contains("owasp") || lower.Contains("scan code"))
            return "security_scan";

        // Compliance
        if (lower.Contains("compliance") || lower.Contains("fedramp") || lower.Contains("nist") || lower.Contains("il5"))
            return "compliance_readiness";

        // Batch processes
        if (lower.Contains("batch") || lower.Contains("scheduled task") || lower.Contains("sql agent") ||
            lower.Contains("windows service") || lower.Contains("ssis") || lower.Contains("etl"))
            return "batch_process_migration";

        // Migration plan
        if (lower.Contains("migration plan") || lower.Contains("roadmap") || lower.Contains("phases") || lower.Contains("timeline"))
            return "migration_plan_generator";

        // General modernization
        if (lower.Contains("modern") || lower.Contains("migrat") || lower.Contains("legacy") || lower.Contains("upgrade"))
            return "general_modernization";

        return "general_modernization";
    }
}
