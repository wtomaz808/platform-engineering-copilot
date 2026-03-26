using System.Text.Json;
using Microsoft.Extensions.Logging;
using Platform.Engineering.Copilot.Agents.Common;

namespace Platform.Engineering.Copilot.Agents.Modernization.Tools;

/// <summary>
/// LLM-assisted security scan of code content. Detects hardcoded secrets, insecure HTTP,
/// weak crypto, SQL injection, XSS, BinaryFormatter, and other OWASP Top 10 patterns.
/// Recommends SAST tooling and remediation steps.
/// </summary>
public class SecurityScanTool : BaseTool
{
    public override string Name => "security_scan";

    public override string Description =>
        "Scan code or application configuration for security vulnerabilities before migrating to Azure Government. " +
        "Detects hardcoded secrets, insecure HTTP, weak cryptography, SQL injection, XSS, BinaryFormatter, " +
        "insecure deserialization, and other OWASP Top 10 patterns. " +
        "Provide code snippets, configuration files, or describe the security patterns in your application.";

    public SecurityScanTool(ILogger<SecurityScanTool> logger) : base(logger)
    {
        Parameters.Add(new ToolParameter("code_content", "Code snippets, configuration content, or description of the application's security patterns. Include connection strings, authentication code, data access patterns, and any known security concerns.", true));
    }

    public override async Task<string> ExecuteAsync(
        IDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var input = GetRequiredString(arguments, "code_content");
        Logger.LogInformation("Running security scan on input ({Length} chars)", input.Length);

        var findings = new List<SecurityFinding>();
        var lower = input.ToLowerInvariant();

        // Hardcoded secrets / credentials
        CheckPattern(findings, input, lower,
            new[] { "password=", "pwd=", "password \"", "secret=", "apikey=", "connectionstring" },
            "Hardcoded Credentials",
            "Potential hardcoded credentials or connection strings detected in code",
            "Critical",
            "A07:2021 — Identification and Authentication Failures",
            new[] { "Move all secrets to Azure Key Vault", "Use Managed Identity for Azure resource authentication", "Implement Azure Key Vault references in App Service configuration" });

        // Insecure HTTP
        CheckPattern(findings, input, lower,
            new[] { "http://", "httpclient(\"http:", "requirehttps = false", "requiressl=\"false\"" },
            "Insecure HTTP",
            "Unencrypted HTTP communication detected — data in transit is not protected",
            "High",
            "A02:2021 — Cryptographic Failures",
            new[] { "Enforce HTTPS everywhere — use HSTS headers", "Configure TLS 1.2+ minimum in Azure App Service", "Use Azure Front Door or Application Gateway for TLS termination" });

        // Weak cryptography
        CheckPattern(findings, input, lower,
            new[] { "md5", "sha1", "des.", "3des", "rc4", "tripledes", "rijndael" },
            "Weak Cryptography",
            "Weak or deprecated cryptographic algorithms detected",
            "High",
            "A02:2021 — Cryptographic Failures",
            new[] { "Replace MD5/SHA1 with SHA-256 or SHA-512", "Replace DES/3DES with AES-256", "Use Azure Key Vault for key management" });

        // SQL Injection
        CheckPattern(findings, input, lower,
            new[] { "string.format(\"select", "\"select * from \" +", "+ \" where ", "string.concat(\"select", "sqlcommand(\"select" },
            "SQL Injection Risk",
            "Potential SQL injection vulnerability — string concatenation in SQL queries",
            "Critical",
            "A03:2021 — Injection",
            new[] { "Use parameterized queries or stored procedures", "Use Entity Framework or Dapper for data access", "Enable Azure SQL Advanced Threat Protection" });

        // XSS
        CheckPattern(findings, input, lower,
            new[] { "response.write(", "innerhtml =", "html.raw(", "@html.raw(", "document.write(" },
            "Cross-Site Scripting (XSS)",
            "Potential XSS vulnerability — unencoded output to HTML",
            "High",
            "A03:2021 — Injection",
            new[] { "Use Razor encoding (@ syntax encodes by default)", "Avoid Html.Raw() with user input", "Implement Content Security Policy (CSP) headers" });

        // BinaryFormatter / insecure deserialization
        CheckPattern(findings, input, lower,
            new[] { "binaryformatter", "soapformatter", "netdatacontractserializer", "losformatter", "objectstateformatter" },
            "Insecure Deserialization",
            "BinaryFormatter or other insecure deserializers detected — remote code execution risk",
            "Critical",
            "A08:2021 — Software and Data Integrity Failures",
            new[] { "Replace BinaryFormatter with System.Text.Json or MessagePack", "Never deserialize untrusted data", "Use type-safe serializers only" });

        // ViewState
        CheckPattern(findings, input, lower,
            new[] { "viewstateencryptionmode", "enableviewstate", "viewstatemac", "__viewstate" },
            "ViewState Security",
            "ASP.NET ViewState detected — ensure MAC validation and encryption are enabled",
            "Medium",
            "A08:2021 — Software and Data Integrity Failures",
            new[] { "Ensure ViewState MAC validation is enabled (do not set EnableViewStateMac=false)", "Enable ViewState encryption for sensitive data", "Consider migrating away from WebForms to eliminate ViewState" });

        // CORS misconfiguration
        CheckPattern(findings, input, lower,
            new[] { "access-control-allow-origin: *", "allowanyorigin", "allowanyheader", "allowanymethod" },
            "CORS Misconfiguration",
            "Overly permissive CORS policy detected — allows any origin",
            "Medium",
            "A05:2021 — Security Misconfiguration",
            new[] { "Restrict CORS to specific trusted origins", "Use Azure Front Door or APIM for CORS policy management", "Never use AllowAnyOrigin with AllowCredentials" });

        // Summary stats
        var criticalCount = findings.Count(f => f.Severity == "Critical");
        var highCount = findings.Count(f => f.Severity == "High");
        var mediumCount = findings.Count(f => f.Severity == "Medium");

        return ToJson(new
        {
            success = true,
            tool = "security_scan",
            summary = new
            {
                totalFindings = findings.Count,
                critical = criticalCount,
                high = highCount,
                medium = mediumCount,
                overallRisk = criticalCount > 0 ? "Critical" : highCount > 0 ? "High" : mediumCount > 0 ? "Medium" : "Low"
            },
            findings = findings.Select(f => new
            {
                f.Category,
                f.Description,
                f.Severity,
                f.OwaspReference,
                f.Remediation
            }).ToList(),
            recommendedTools = new[]
            {
                new { tool = "GitHub Advanced Security", description = "Code scanning with CodeQL, secret scanning, dependency review" },
                new { tool = "Microsoft Defender for DevOps", description = "Connects to Azure DevOps and GitHub for security posture management" },
                new { tool = "SonarQube / SonarCloud", description = "Static analysis for code quality and security — supports .NET" },
                new { tool = "OWASP ZAP", description = "Dynamic application security testing (DAST) — test running applications" },
                new { tool = "NuGet Audit", description = "Built-in .NET vulnerability scanning for NuGet dependencies (dotnet restore --audit)" }
            },
            azureGovSecurityServices = new[]
            {
                "Microsoft Defender for Cloud — Unified security management and threat protection",
                "Azure Key Vault — HSM-backed secret, key, and certificate management",
                "Azure DDoS Protection — Network layer DDoS mitigation",
                "Azure Firewall — Cloud-native network firewall with threat intelligence",
                "Azure Policy — Enforce security and compliance guardrails",
                "Azure Sentinel — Cloud-native SIEM for threat detection and response"
            },
            userInput = input
        });
    }

    private void CheckPattern(
        List<SecurityFinding> findings,
        string input,
        string lower,
        string[] patterns,
        string category,
        string description,
        string severity,
        string owaspRef,
        string[] remediation)
    {
        if (patterns.Any(p => lower.Contains(p)))
        {
            findings.Add(new SecurityFinding
            {
                Category = category,
                Description = description,
                Severity = severity,
                OwaspReference = owaspRef,
                Remediation = remediation.ToList()
            });
        }
    }

    private class SecurityFinding
    {
        public string Category { get; set; } = "";
        public string Description { get; set; } = "";
        public string Severity { get; set; } = "";
        public string OwaspReference { get; set; } = "";
        public List<string> Remediation { get; set; } = new();
    }
}
