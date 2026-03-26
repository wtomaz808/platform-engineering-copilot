using System.ComponentModel.DataAnnotations;

namespace Platform.Engineering.Copilot.Agents.Modernization.Configuration;

/// <summary>
/// Configuration options for the Modernization &amp; Migration Agent.
/// </summary>
public class ModernizationAgentOptions
{
    public const string SectionName = "AgentConfiguration:ModernizationAgent";

    /// <summary>
    /// Whether this agent is enabled. When false, the agent will not be registered.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Temperature for AI responses (0.0 - 2.0).
    /// Lower = more focused and deterministic. Default: 0.3
    /// </summary>
    [Range(0.0, 2.0)]
    public double Temperature { get; set; } = 0.3;

    /// <summary>
    /// Maximum tokens for chat completion requests. Default: 16000
    /// </summary>
    [Range(1, 128000)]
    public int MaxTokens { get; set; } = 16000;

    /// <summary>
    /// Default target cloud environment for migration recommendations.
    /// Options: "AzureGovernment", "AzurePublicCloud"
    /// </summary>
    public string DefaultTargetCloud { get; set; } = "AzureGovernment";

    /// <summary>
    /// Default compliance framework for readiness checks.
    /// Options: "FedRAMP-High", "FedRAMP-Moderate", "DoD-IL5", "DoD-IL4"
    /// </summary>
    public string DefaultComplianceFramework { get; set; } = "FedRAMP-High";
}
