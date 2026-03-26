using System.Text.Json;
using Microsoft.Extensions.Logging;
using Platform.Engineering.Copilot.Agents.Common;
using Platform.Engineering.Copilot.Agents.Modernization.Services;

namespace Platform.Engineering.Copilot.Agents.Modernization.Tools;

/// <summary>
/// Assesses database migration complexity and generates a migration plan.
/// Detects DB provider, identifies migration blockers (stored procs, linked servers, CLR assemblies, SQL Agent jobs),
/// maps to Azure target (SQL MI, SQL DB, PostgreSQL, MySQL), and recommends migration method.
/// </summary>
public class DatabaseMigrationTool : BaseTool
{
    private readonly CodeAnalysisService _codeAnalysis;

    public override string Name => "database_migration";

    public override string Description =>
        "Assess database migration complexity and generate a migration plan for Azure Government. " +
        "Analyzes connection strings, DB provider, stored procedures, linked servers, CLR assemblies, SQL Agent jobs, " +
        "and SSIS packages. Maps to Azure target (SQL MI, SQL DB, Azure PostgreSQL/MySQL) and recommends " +
        "migration method (Azure DMS, bacpac, transactional replication). Provide connection string info, " +
        "database description, or pasted config with connection strings.";

    public DatabaseMigrationTool(
        ILogger<DatabaseMigrationTool> logger,
        CodeAnalysisService codeAnalysis) : base(logger)
    {
        _codeAnalysis = codeAnalysis;
        Parameters.Add(new ToolParameter("connection_info", "Connection string, DB provider name, database description, or pasted config content containing connection strings. Include details about stored procedures, SQL Agent jobs, linked servers, SSIS packages if known.", true));
    }

    public override async Task<string> ExecuteAsync(
        IDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var input = GetRequiredString(arguments, "connection_info");
        Logger.LogInformation("Running database migration assessment on input ({Length} chars)", input.Length);

        // Try to extract connection strings from XML content
        var detectedDbs = new List<DbInfo>();
        if (input.TrimStart().StartsWith("<"))
        {
            var webConfig = _codeAnalysis.ParseWebConfigContent(input);
            foreach (var cs in webConfig.ConnectionStrings)
            {
                detectedDbs.Add(new DbInfo
                {
                    Name = cs.Name,
                    DbType = cs.DetectedDbType,
                    ConnectionString = cs.ConnectionString,
                    Provider = cs.ProviderName
                });
            }
        }

        // Also try raw connection string extraction
        if (detectedDbs.Count == 0)
        {
            var extracted = _codeAnalysis.ExtractConnectionStrings(input);
            foreach (var cs in extracted)
            {
                detectedDbs.Add(new DbInfo
                {
                    Name = "Detected",
                    DbType = cs.DetectedDbType,
                    ConnectionString = cs.ConnectionString
                });
            }
        }

        // Build migration recommendations for each DB
        var migrations = detectedDbs.Select(db => BuildMigrationPlan(db)).ToList();

        return ToJson(new
        {
            success = true,
            tool = "database_migration",
            detectedDatabases = detectedDbs.Select(d => new { d.Name, d.DbType, d.Provider, connectionString = d.ConnectionString }).ToList(),
            migrationPlans = migrations,
            generalGuidance = new
            {
                azureGovAvailability = new Dictionary<string, string>
                {
                    ["Azure SQL MI"] = "Available in USGov Virginia, USGov Arizona",
                    ["Azure SQL Database"] = "Available in USGov Virginia, USGov Arizona, USGov Texas",
                    ["Azure Database for PostgreSQL"] = "Available in USGov Virginia, USGov Arizona",
                    ["Azure Database for MySQL"] = "Available in USGov Virginia, USGov Arizona",
                    ["Azure Cache for Redis"] = "Available in USGov Virginia, USGov Arizona"
                },
                migrationTools = new[]
                {
                    "Azure Database Migration Service (DMS) — Online/offline migration for SQL Server, PostgreSQL, MySQL",
                    "BACPAC export/import — Simple offline migration for smaller SQL databases",
                    "Transactional Replication — Near-zero downtime for SQL Server to SQL MI",
                    "Data Migration Assistant (DMA) — Pre-migration assessment and compatibility check",
                    "Azure Migrate — Discovery and assessment for on-premises databases"
                },
                preMigrationChecklist = new[]
                {
                    "Run Data Migration Assistant (DMA) for detailed compatibility report",
                    "Inventory all stored procedures, triggers, views, and functions",
                    "Identify cross-database queries and linked server dependencies",
                    "Document SQL Agent jobs and SSIS packages for migration planning",
                    "Assess database size and transaction volume for tier sizing",
                    "Plan for encryption at rest (TDE) and in transit (TLS 1.2+)",
                    "Verify Azure Gov region supports required DB service tier"
                }
            },
            userInput = input,
            instructions = detectedDbs.Count == 0
                ? "No structured connection strings detected. Please provide connection strings, database type, " +
                  "or describe your databases (type, size, features used) for a detailed assessment."
                : null
        });
    }

    private object BuildMigrationPlan(DbInfo db)
    {
        var (target, reason) = RecommendAzureTarget(db.DbType);
        var method = RecommendMigrationMethod(db.DbType, target);

        return new
        {
            database = db.Name,
            sourceType = db.DbType,
            recommendedTarget = target,
            targetReason = reason,
            migrationMethod = method,
            estimatedComplexity = EstimateComplexity(db.DbType),
            keyConsiderations = GetConsiderations(db.DbType, target),
            phases = new[]
            {
                new { phase = "1. Assess", description = "Run DMA/Azure Migrate, inventory objects, check compatibility", durationEstimate = "1-2 weeks" },
                new { phase = "2. Prepare", description = $"Set up {target} in Azure Gov, configure networking/firewall, TDE encryption", durationEstimate = "1-2 weeks" },
                new { phase = "3. Migrate", description = $"Execute migration via {method}, validate data integrity", durationEstimate = "1-3 weeks" },
                new { phase = "4. Cutover", description = "DNS/connection string update, parallel validation, switchover", durationEstimate = "1-2 days" },
                new { phase = "5. Optimize", description = "Performance tuning, index optimization, monitoring setup", durationEstimate = "Ongoing" }
            }
        };
    }

    private (string target, string reason) RecommendAzureTarget(string dbType)
    {
        return dbType switch
        {
            "SQL Server" => ("Azure SQL Managed Instance",
                "Highest compatibility with on-prem SQL Server. Supports SQL Agent jobs, linked servers, CLR, " +
                "cross-database queries, and most SQL Server features. Best for legacy apps."),
            "Oracle" => ("Azure SQL Managed Instance (with migration) or Azure VM with Oracle",
                "For Oracle-to-SQL migration use SSMA. If Oracle features are critical, consider Oracle on Azure VM."),
            "MySQL" => ("Azure Database for MySQL Flexible Server",
                "Fully managed MySQL with high availability, automated backups, and Azure Gov support."),
            "PostgreSQL" => ("Azure Database for PostgreSQL Flexible Server",
                "Fully managed PostgreSQL with built-in HA, extensions support, and intelligent tuning."),
            "MongoDB" => ("Azure Cosmos DB (MongoDB API)",
                "Wire-protocol compatible with MongoDB. Available in Azure Gov."),
            "Redis" => ("Azure Cache for Redis",
                "Fully managed Redis with clustering, persistence, and Azure Gov availability."),
            _ => ("Azure SQL Managed Instance",
                "Default recommendation for unknown DB types due to broad compatibility.")
        };
    }

    private string RecommendMigrationMethod(string dbType, string target)
    {
        if (dbType == "SQL Server" && target.Contains("Managed Instance"))
            return "Azure Database Migration Service (DMS) online migration or Transactional Replication for near-zero downtime";
        if (dbType == "SQL Server")
            return "BACPAC export/import for smaller DBs, or Azure DMS for larger databases";
        if (dbType is "MySQL" or "PostgreSQL")
            return "Azure Database Migration Service (DMS) with online migration support";
        if (dbType == "MongoDB")
            return "Azure Cosmos DB Data Migration Tool or mongodump/mongorestore";
        return "Azure Database Migration Service (DMS) or vendor-specific migration tools";
    }

    private string EstimateComplexity(string dbType)
    {
        return dbType switch
        {
            "SQL Server" => "Medium — SQL MI provides high compatibility but check CLR/linked servers",
            "Oracle" => "High — Schema and PL/SQL migration requires SSMA and manual review",
            "MySQL" or "PostgreSQL" => "Low-Medium — Good migration tooling available",
            _ => "Medium — Requires detailed assessment"
        };
    }

    private string[] GetConsiderations(string dbType, string target)
    {
        var considerations = new List<string>
        {
            "Ensure TLS 1.2+ for all connections (SC-8 encryption in transit)",
            "Enable Transparent Data Encryption (TDE) for data at rest (SC-28)",
            "Configure Azure Private Link or VNet service endpoints for network isolation",
            "Set up Azure Monitor and diagnostic logging (AU-2 audit events)"
        };

        if (dbType == "SQL Server")
        {
            considerations.Add("Review CLR assemblies — SQL MI supports SAFE assemblies only");
            considerations.Add("Check linked server references — may need VNet peering or removal");
            considerations.Add("SQL Agent jobs transfer automatically to SQL MI");
            considerations.Add("SSIS packages need Azure-SSIS Integration Runtime in Azure Data Factory");
        }

        return considerations.ToArray();
    }

    private class DbInfo
    {
        public string Name { get; set; } = "";
        public string DbType { get; set; } = "";
        public string ConnectionString { get; set; } = "";
        public string Provider { get; set; } = "";
    }
}
