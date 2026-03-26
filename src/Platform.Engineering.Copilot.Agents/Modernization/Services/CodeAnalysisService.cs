using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace Platform.Engineering.Copilot.Agents.Modernization.Services;

/// <summary>
/// Shared service for parsing .NET project files, web.config, packages.config,
/// connection strings, and other on-prem application artifacts.
/// </summary>
public class CodeAnalysisService
{
    private readonly ILogger<CodeAnalysisService> _logger;

    public CodeAnalysisService(ILogger<CodeAnalysisService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Parse a .csproj file and extract framework, packages, and references.
    /// </summary>
    public ProjectAnalysis ParseCsprojContent(string xml)
    {
        var result = new ProjectAnalysis();
        try
        {
            var doc = XDocument.Parse(xml);
            var ns = doc.Root?.Name.Namespace ?? XNamespace.None;

            // SDK-style (net6.0+) or legacy?
            var sdkAttr = doc.Root?.Attribute("Sdk")?.Value;
            result.IsSdkStyle = sdkAttr != null;

            // Target framework
            result.TargetFramework = doc.Descendants(ns + "TargetFramework").FirstOrDefault()?.Value
                ?? doc.Descendants(ns + "TargetFrameworkVersion").FirstOrDefault()?.Value
                ?? "Unknown";

            // Target frameworks (multi-target)
            var multiTarget = doc.Descendants(ns + "TargetFrameworks").FirstOrDefault()?.Value;
            if (!string.IsNullOrEmpty(multiTarget))
                result.TargetFrameworks = multiTarget.Split(';').ToList();

            // PackageReferences (SDK-style)
            foreach (var pkgRef in doc.Descendants(ns + "PackageReference"))
            {
                var name = pkgRef.Attribute("Include")?.Value ?? "";
                var version = pkgRef.Attribute("Version")?.Value ?? pkgRef.Element(ns + "Version")?.Value ?? "";
                if (!string.IsNullOrEmpty(name))
                    result.PackageReferences.Add(new PackageInfo { Name = name, Version = version });
            }

            // COM references
            foreach (var comRef in doc.Descendants(ns + "COMReference"))
            {
                var name = comRef.Attribute("Include")?.Value ?? comRef.Element(ns + "Include")?.Value ?? "";
                if (!string.IsNullOrEmpty(name))
                    result.ComReferences.Add(name);
            }

            // GAC / assembly references
            foreach (var asmRef in doc.Descendants(ns + "Reference"))
            {
                var name = asmRef.Attribute("Include")?.Value ?? "";
                var hintPath = asmRef.Element(ns + "HintPath")?.Value;
                if (!string.IsNullOrEmpty(name))
                    result.AssemblyReferences.Add(new AssemblyRefInfo { Name = name, HintPath = hintPath });
            }

            // Project references
            foreach (var projRef in doc.Descendants(ns + "ProjectReference"))
            {
                var include = projRef.Attribute("Include")?.Value ?? "";
                if (!string.IsNullOrEmpty(include))
                    result.ProjectReferences.Add(include);
            }

            // Output type
            result.OutputType = doc.Descendants(ns + "OutputType").FirstOrDefault()?.Value ?? "Library";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse .csproj content");
            result.ParseErrors.Add($"Failed to parse .csproj: {ex.Message}");
        }
        return result;
    }

    /// <summary>
    /// Parse web.config or app.config to extract connection strings, IIS modules, auth, WCF, etc.
    /// </summary>
    public WebConfigAnalysis ParseWebConfigContent(string xml)
    {
        var result = new WebConfigAnalysis();
        try
        {
            var doc = XDocument.Parse(xml);

            // Connection strings
            foreach (var cs in doc.Descendants("add").Where(e => e.Parent?.Name.LocalName == "connectionStrings"))
            {
                var name = cs.Attribute("name")?.Value ?? "";
                var connStr = cs.Attribute("connectionString")?.Value ?? "";
                var provider = cs.Attribute("providerName")?.Value ?? "";
                result.ConnectionStrings.Add(new ConnectionStringInfo
                {
                    Name = name,
                    ConnectionString = SanitizeConnectionString(connStr),
                    ProviderName = provider,
                    DetectedDbType = DetectDbType(connStr, provider)
                });
            }

            // IIS modules
            foreach (var module in doc.Descendants("add").Where(e =>
                e.Parent?.Name.LocalName == "modules" || e.Parent?.Name.LocalName == "httpModules"))
            {
                var name = module.Attribute("name")?.Value ?? module.Attribute("type")?.Value ?? "";
                if (!string.IsNullOrEmpty(name))
                    result.IisModules.Add(name);
            }

            // IIS handlers
            foreach (var handler in doc.Descendants("add").Where(e =>
                e.Parent?.Name.LocalName == "handlers" || e.Parent?.Name.LocalName == "httpHandlers"))
            {
                var name = handler.Attribute("name")?.Value ?? handler.Attribute("type")?.Value ?? "";
                if (!string.IsNullOrEmpty(name))
                    result.IisHandlers.Add(name);
            }

            // Authentication mode
            var authNode = doc.Descendants("authentication").FirstOrDefault();
            result.AuthenticationMode = authNode?.Attribute("mode")?.Value ?? "None";

            // WCF services
            result.HasWcf = doc.Descendants("system.serviceModel").Any();
            foreach (var svc in doc.Descendants("service"))
            {
                var name = svc.Attribute("name")?.Value ?? "";
                if (!string.IsNullOrEmpty(name))
                    result.WcfServices.Add(name);
            }

            // WCF bindings
            foreach (var binding in doc.Descendants().Where(e =>
                e.Name.LocalName.EndsWith("Binding") && e.Parent?.Name.LocalName == "bindings"))
            {
                result.WcfBindings.Add(binding.Name.LocalName);
            }

            // MSMQ
            result.HasMsmq = doc.Descendants().Any(e =>
                e.Value?.Contains("msmq", StringComparison.OrdinalIgnoreCase) == true ||
                e.Name.LocalName.Contains("msmq", StringComparison.OrdinalIgnoreCase));

            // App settings (keys only, not values - security)
            foreach (var setting in doc.Descendants("add").Where(e => e.Parent?.Name.LocalName == "appSettings"))
            {
                var key = setting.Attribute("key")?.Value ?? "";
                if (!string.IsNullOrEmpty(key))
                    result.AppSettingKeys.Add(key);
            }

            // Compilation target framework
            var compilation = doc.Descendants("compilation").FirstOrDefault();
            result.CompilationTargetFramework = compilation?.Attribute("targetFramework")?.Value;
            result.CompilationDebug = compilation?.Attribute("debug")?.Value == "true";

            // Custom errors mode
            result.CustomErrorsMode = doc.Descendants("customErrors").FirstOrDefault()?.Attribute("mode")?.Value;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse web.config content");
            result.ParseErrors.Add($"Failed to parse web.config: {ex.Message}");
        }
        return result;
    }

    /// <summary>
    /// Parse packages.config to extract NuGet packages.
    /// </summary>
    public List<PackageInfo> ParsePackagesConfig(string xml)
    {
        var packages = new List<PackageInfo>();
        try
        {
            var doc = XDocument.Parse(xml);
            foreach (var pkg in doc.Descendants("package"))
            {
                packages.Add(new PackageInfo
                {
                    Name = pkg.Attribute("id")?.Value ?? "",
                    Version = pkg.Attribute("version")?.Value ?? "",
                    TargetFramework = pkg.Attribute("targetFramework")?.Value
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse packages.config");
        }
        return packages;
    }

    /// <summary>
    /// Extract and classify connection strings from any text content.
    /// </summary>
    public List<ConnectionStringInfo> ExtractConnectionStrings(string content)
    {
        var results = new List<ConnectionStringInfo>();
        // Match common connection string patterns
        var patterns = new[]
        {
            @"(?:Server|Data Source)=([^;""]+);.*?(?:Database|Initial Catalog)=([^;""]+)",
            @"(?:Host|Server)=([^;""]+);.*?(?:Database)=([^;""]+)",
            @"mongodb(?:\+srv)?://[^""'\s]+",
            @"redis://[^""'\s]+"
        };

        foreach (var pattern in patterns)
        {
            foreach (Match match in Regex.Matches(content, pattern, RegexOptions.IgnoreCase))
            {
                var connStr = match.Value;
                results.Add(new ConnectionStringInfo
                {
                    ConnectionString = SanitizeConnectionString(connStr),
                    DetectedDbType = DetectDbTypeFromString(connStr)
                });
            }
        }
        return results;
    }

    /// <summary>
    /// Detect database type from connection string and provider.
    /// </summary>
    public string DetectDbType(string connectionString, string providerName)
    {
        if (!string.IsNullOrEmpty(providerName))
        {
            if (providerName.Contains("SqlClient", StringComparison.OrdinalIgnoreCase)) return "SQL Server";
            if (providerName.Contains("Oracle", StringComparison.OrdinalIgnoreCase)) return "Oracle";
            if (providerName.Contains("MySql", StringComparison.OrdinalIgnoreCase)) return "MySQL";
            if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ||
                providerName.Contains("Postgre", StringComparison.OrdinalIgnoreCase)) return "PostgreSQL";
        }
        return DetectDbTypeFromString(connectionString);
    }

    private string DetectDbTypeFromString(string connectionString)
    {
        var lower = connectionString.ToLowerInvariant();
        if (lower.Contains("sqlserver") || lower.Contains("mssql") ||
            lower.Contains("initial catalog") || lower.Contains(".database.windows") ||
            lower.Contains("data source=")) return "SQL Server";
        if (lower.Contains("oracle") || lower.Contains("ora:")) return "Oracle";
        if (lower.Contains("mysql") || lower.Contains("port=3306")) return "MySQL";
        if (lower.Contains("postgres") || lower.Contains("npgsql") || lower.Contains("port=5432")) return "PostgreSQL";
        if (lower.Contains("mongodb")) return "MongoDB";
        if (lower.Contains("redis")) return "Redis";
        return "Unknown";
    }

    /// <summary>
    /// Remove passwords/secrets from connection strings for safe display.
    /// </summary>
    public string SanitizeConnectionString(string connectionString)
    {
        var sanitized = Regex.Replace(connectionString,
            @"(Password|Pwd|Secret|AccountKey)=([^;""]+)",
            "$1=***REDACTED***",
            RegexOptions.IgnoreCase);
        return sanitized;
    }

    // --- Data models ---

    public class ProjectAnalysis
    {
        public bool IsSdkStyle { get; set; }
        public string TargetFramework { get; set; } = "";
        public List<string> TargetFrameworks { get; set; } = new();
        public string OutputType { get; set; } = "";
        public List<PackageInfo> PackageReferences { get; set; } = new();
        public List<string> ComReferences { get; set; } = new();
        public List<AssemblyRefInfo> AssemblyReferences { get; set; } = new();
        public List<string> ProjectReferences { get; set; } = new();
        public List<string> ParseErrors { get; set; } = new();
    }

    public class WebConfigAnalysis
    {
        public List<ConnectionStringInfo> ConnectionStrings { get; set; } = new();
        public List<string> IisModules { get; set; } = new();
        public List<string> IisHandlers { get; set; } = new();
        public string AuthenticationMode { get; set; } = "";
        public bool HasWcf { get; set; }
        public List<string> WcfServices { get; set; } = new();
        public List<string> WcfBindings { get; set; } = new();
        public bool HasMsmq { get; set; }
        public List<string> AppSettingKeys { get; set; } = new();
        public string? CompilationTargetFramework { get; set; }
        public bool CompilationDebug { get; set; }
        public string? CustomErrorsMode { get; set; }
        public List<string> ParseErrors { get; set; } = new();
    }

    public class PackageInfo
    {
        public string Name { get; set; } = "";
        public string Version { get; set; } = "";
        public string? TargetFramework { get; set; }
    }

    public class ConnectionStringInfo
    {
        public string Name { get; set; } = "";
        public string ConnectionString { get; set; } = "";
        public string ProviderName { get; set; } = "";
        public string DetectedDbType { get; set; } = "";
    }

    public class AssemblyRefInfo
    {
        public string Name { get; set; } = "";
        public string? HintPath { get; set; }
    }
}
