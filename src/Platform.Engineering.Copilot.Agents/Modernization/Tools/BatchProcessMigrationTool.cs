using System.Text.Json;
using Microsoft.Extensions.Logging;
using Platform.Engineering.Copilot.Agents.Common;

namespace Platform.Engineering.Copilot.Agents.Modernization.Tools;

/// <summary>
/// Analyzes batch processes, scheduled tasks, Windows Services, SQL Agent jobs, and console apps.
/// Maps each to Azure Functions, Azure Batch, Data Factory, WebJobs, Durable Functions, Logic Apps,
/// or Elastic Jobs. Provides migration path, trigger mapping, and code conversion guidance.
/// </summary>
public class BatchProcessMigrationTool : BaseTool
{
    public override string Name => "batch_process_migration";

    public override string Description =>
        "Analyze batch processes and scheduled tasks for migration to Azure Government. " +
        "Evaluates .bat/.cmd/.ps1 scripts, Task Scheduler jobs, SQL Agent jobs, console applications, " +
        "Windows Services, and SSIS packages. Maps each to the appropriate Azure service " +
        "(Azure Functions, Azure Batch, Data Factory, WebJobs, Durable Functions, Logic Apps, Elastic Jobs). " +
        "Provide descriptions of your batch processes including trigger type, frequency, dependencies, and purpose.";

    public BatchProcessMigrationTool(ILogger<BatchProcessMigrationTool> logger) : base(logger)
    {
        Parameters.Add(new ToolParameter("batch_info", "Description of batch processes: script types (.bat, .ps1, console app), triggers (Task Scheduler, SQL Agent, cron), frequency, duration, dependencies, data sources/targets, and purpose. Include any Windows Service or SSIS package details.", true));
    }

    public override async Task<string> ExecuteAsync(
        IDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var input = GetRequiredString(arguments, "batch_info");
        Logger.LogInformation("Analyzing batch processes for migration ({Length} chars)", input.Length);

        var lower = input.ToLowerInvariant();
        var detectedProcesses = new List<object>();

        // Task Scheduler / cron jobs
        if (lower.Contains("task scheduler") || lower.Contains("scheduled task") || lower.Contains("cron"))
        {
            detectedProcesses.Add(BuildProcessMapping(
                "Windows Task Scheduler / Cron Job",
                "Timer-triggered scheduled task",
                DetermineTarget(lower, "scheduled"),
                "CRON expression mapping from Task Scheduler to Azure Functions Timer trigger",
                new[]
                {
                    "Map Task Scheduler trigger to CRON expression for Azure Functions",
                    "Convert script logic to C#/PowerShell Azure Function",
                    "Use Azure Key Vault for any credentials used by the script",
                    "Configure Application Insights for monitoring and alerting"
                }));
        }

        // SQL Agent jobs
        if (lower.Contains("sql agent") || lower.Contains("sqlagent") || lower.Contains("sql server agent"))
        {
            detectedProcesses.Add(BuildProcessMapping(
                "SQL Server Agent Jobs",
                "Database maintenance, ETL, or scheduled queries",
                "Azure SQL MI (built-in Agent) or Elastic Jobs for Azure SQL DB",
                "SQL Agent jobs migrate 1:1 to SQL MI. For Azure SQL DB, use Elastic Jobs or Azure Data Factory.",
                new[]
                {
                    "SQL MI: SQL Agent jobs transfer automatically during migration",
                    "Azure SQL DB: Convert to Elastic Jobs (T-SQL), Data Factory pipelines, or Azure Functions",
                    "Move SSIS packages to Azure-SSIS Integration Runtime in Data Factory",
                    "Replace linked server references with Azure networking (VNet peering, Private Link)"
                }));
        }

        // Console applications
        if (lower.Contains("console app") || lower.Contains("console application") || lower.Contains(".exe"))
        {
            detectedProcesses.Add(BuildProcessMapping(
                "Console Applications (.exe)",
                "Standalone executable performing batch operations",
                DetermineTarget(lower, "console"),
                "Containerize and run as Azure Container Instance, or convert to Azure Function",
                new[]
                {
                    "Short-running (<10 min): Convert to Azure Functions with timer/queue trigger",
                    "Long-running: Containerize and run as Azure Container Instance or Azure Batch task",
                    "Stateful workflows: Use Durable Functions for orchestration",
                    "Replace file system I/O with Azure Blob Storage / Azure Files",
                    "Replace database connections with Azure-compatible connection strings + Managed Identity"
                }));
        }

        // PowerShell / batch scripts
        if (lower.Contains(".ps1") || lower.Contains("powershell") || lower.Contains(".bat") || lower.Contains(".cmd") || lower.Contains("batch script"))
        {
            detectedProcesses.Add(BuildProcessMapping(
                "PowerShell / Batch Scripts",
                "Automation scripts for file processing, system tasks, or data operations",
                "Azure Functions (PowerShell) or Azure Automation Runbooks",
                "PowerShell scripts often map directly to Azure Functions (PowerShell runtime) or Azure Automation",
                new[]
                {
                    "Azure Functions: Best for event-driven or timer-triggered scripts",
                    "Azure Automation: Best for infrastructure ops, VM management, and complex runbooks",
                    "Replace local file paths with Azure Storage paths",
                    "Use Az PowerShell module instead of local AD/server cmdlets",
                    "Store credentials in Azure Key Vault; use Get-AzKeyVaultSecret"
                }));
        }

        // Windows Services
        if (lower.Contains("windows service") || lower.Contains("topshelf") || lower.Contains("service control"))
        {
            detectedProcesses.Add(BuildProcessMapping(
                "Windows Services",
                "Long-running background process",
                "Azure WebJobs (continuous) or Container Apps with IHostedService",
                "Windows Services are best migrated to Worker Services using IHostedService",
                new[]
                {
                    "Convert to .NET Worker Service (IHostedService) pattern",
                    "Deploy as continuous WebJob in App Service, or as container in Container Apps/AKS",
                    "Use Azure Service Bus for queue-driven processing instead of polling",
                    "Implement graceful shutdown handling for container orchestrators",
                    "Add health check endpoints for orchestrator probing"
                }));
        }

        // SSIS packages
        if (lower.Contains("ssis") || lower.Contains("integration services") || lower.Contains("dtsx"))
        {
            detectedProcesses.Add(BuildProcessMapping(
                "SSIS Packages",
                "ETL / data integration pipelines",
                "Azure Data Factory with Azure-SSIS Integration Runtime",
                "Lift-and-shift SSIS packages to Azure-SSIS IR, or modernize to ADF data flows",
                new[]
                {
                    "Azure-SSIS IR: Run existing SSIS packages in Azure with minimal changes",
                    "Azure Data Factory mapping data flows: Modernize ETL with visual data transformation",
                    "Store SSIS package configurations in Azure SQL MI (SSISDB catalog)",
                    "Replace on-prem data sources with Azure equivalents + Private Link",
                    "Schedule with ADF triggers instead of SQL Agent"
                }));
        }

        // ETL / data processing (generic)
        if (lower.Contains("etl") || lower.Contains("data pipeline") || lower.Contains("data processing") || lower.Contains("data migration"))
        {
            if (!lower.Contains("ssis")) // Avoid duplicate if SSIS already detected
            {
                detectedProcesses.Add(BuildProcessMapping(
                    "ETL / Data Processing Pipelines",
                    "Data extraction, transformation, and loading operations",
                    "Azure Data Factory or Azure Synapse Pipeline",
                    "Modernize ETL to cloud-native data pipelines",
                    new[]
                    {
                        "Azure Data Factory: Best for diverse data source integration and orchestration",
                        "Azure Synapse: Best when combined with analytics/data warehouse workloads",
                        "Use copy activities for data movement, data flows for transformation",
                        "Leverage Self-Hosted Integration Runtime for hybrid connectivity",
                        "Monitor with ADF built-in monitoring and Azure Monitor integration"
                    }));
            }
        }

        // Default if nothing specific detected
        if (detectedProcesses.Count == 0)
        {
            detectedProcesses.Add(BuildProcessMapping(
                "General Batch Process",
                "Scheduled or triggered background task",
                "Azure Functions (Timer trigger) — default recommendation",
                "Provide more details about your batch processes for specific guidance",
                new[]
                {
                    "Short tasks (<10 min): Azure Functions with Timer or Queue trigger",
                    "Long tasks (10 min - hours): Azure Container Instances or Azure Batch",
                    "Complex workflows: Durable Functions for multi-step orchestration",
                    "Infrastructure ops: Azure Automation Runbooks",
                    "Data processing: Azure Data Factory pipelines"
                }));
        }

        return ToJson(new
        {
            success = true,
            tool = "batch_process_migration",
            detectedProcesses,
            azureServiceComparison = new[]
            {
                new { service = "Azure Functions (Timer)", bestFor = "Short tasks (<10 min), event-driven, serverless", triggerTypes = "Timer, Queue, Blob, HTTP, Event Grid", costModel = "Pay-per-execution (consumption) or Premium plan" },
                new { service = "Azure Durable Functions", bestFor = "Multi-step workflows, fan-out/fan-in, human interaction", triggerTypes = "Orchestrator, Activity, Entity functions", costModel = "Pay-per-execution with state management" },
                new { service = "Azure Batch", bestFor = "High-performance compute, parallel processing, long-running jobs", triggerTypes = "Schedule, API-triggered, Data Factory", costModel = "VM pool pricing (Spot VMs for cost savings)" },
                new { service = "Azure WebJobs", bestFor = "Background tasks tied to App Service, continuous processing", triggerTypes = "Timer, Queue, Blob, continuous", costModel = "Included with App Service plan" },
                new { service = "Azure Data Factory", bestFor = "ETL/ELT pipelines, data integration, SSIS migration", triggerTypes = "Schedule, Tumbling window, Event", costModel = "Per-activity run + data movement pricing" },
                new { service = "Azure Logic Apps", bestFor = "Integration workflows, API orchestration, low-code automation", triggerTypes = "Schedule, HTTP, 400+ connectors", costModel = "Per-action execution (consumption) or Standard plan" },
                new { service = "Azure Automation", bestFor = "Infrastructure ops, VM management, runbooks", triggerTypes = "Schedule, Webhook, Azure alerts", costModel = "Per-job minute (500 min/month free)" },
                new { service = "Elastic Jobs (Azure SQL)", bestFor = "T-SQL job scheduling across Azure SQL databases", triggerTypes = "Schedule (CRON), manual", costModel = "Included with Azure SQL" }
            },
            azureGovAvailability = new Dictionary<string, string>
            {
                ["Azure Functions"] = "Available — Consumption, Premium, and Dedicated plans in USGov Virginia, Arizona",
                ["Azure Batch"] = "Available in USGov Virginia, USGov Arizona",
                ["Azure Data Factory"] = "Available in USGov Virginia, USGov Arizona",
                ["Azure Logic Apps"] = "Available — Consumption and Standard in USGov Virginia, Arizona",
                ["Azure Automation"] = "Available in USGov Virginia, USGov Arizona, USGov Texas",
                ["Azure WebJobs"] = "Available (part of App Service) in all Gov regions"
            },
            recommendations = new[]
            {
                "Start by inventorying all scheduled tasks (Get-ScheduledTask) and SQL Agent jobs",
                "Classify each by execution time, trigger type, and dependencies",
                "Use Azure Functions for most short-running tasks — lowest migration effort",
                "Convert Windows Services to .NET Worker Service (IHostedService) pattern first",
                "Replace local file system access with Azure Blob Storage / Azure Files",
                "Use Managed Identity for all Azure resource access — no stored credentials"
            },
            userInput = input
        });
    }

    private string DetermineTarget(string input, string processType)
    {
        if (input.Contains("long-running") || input.Contains("hours") || input.Contains("heavy compute"))
            return "Azure Batch or Azure Container Instances";
        if (input.Contains("workflow") || input.Contains("multi-step") || input.Contains("orchestrat"))
            return "Azure Durable Functions";
        if (input.Contains("queue") || input.Contains("message"))
            return "Azure Functions (Queue trigger)";
        if (processType == "console" && (input.Contains("data") || input.Contains("etl")))
            return "Azure Data Factory";
        return "Azure Functions (Timer trigger)";
    }

    private object BuildProcessMapping(
        string sourceType,
        string description,
        string recommendedTarget,
        string migrationNotes,
        string[] migrationSteps)
    {
        return new
        {
            sourceType,
            description,
            recommendedTarget,
            migrationNotes,
            migrationSteps,
            estimatedEffort = EstimateEffort(sourceType)
        };
    }

    private string EstimateEffort(string sourceType)
    {
        return sourceType switch
        {
            _ when sourceType.Contains("SQL Agent") => "Low (auto-migrates to SQL MI) or Medium (Elastic Jobs)",
            _ when sourceType.Contains("SSIS") => "Medium (lift-and-shift to Azure-SSIS IR) or High (modernize to ADF)",
            _ when sourceType.Contains("Windows Service") => "Medium (rewrite to IHostedService pattern)",
            _ when sourceType.Contains("PowerShell") => "Low-Medium (direct mapping to Azure Functions/Automation)",
            _ when sourceType.Contains("Console") => "Medium (containerize or convert to Function)",
            _ => "Medium"
        };
    }
}
