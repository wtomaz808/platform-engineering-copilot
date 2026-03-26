using System.Text.Json;
using Microsoft.Extensions.Logging;
using Platform.Engineering.Copilot.Agents.Common;

namespace Platform.Engineering.Copilot.Agents.Modernization.Tools;

/// <summary>
/// Assesses containerization readiness, generates Dockerfiles, and recommends
/// Azure container hosting target (ACR + AKS vs ACI vs Container Apps).
/// </summary>
public class ContainerizationAssessmentTool : BaseTool
{
    public override string Name => "containerization_assessment";

    public override string Description =>
        "Assess whether a .NET application is ready for containerization and recommend Azure container hosting. " +
        "Evaluates blockers (file system state, Windows registry, GAC, COM+, Windows Services, local certs). " +
        "Generates a Dockerfile template and recommends ACR + AKS, ACI, or Container Apps. " +
        "Provide the app description, project file analysis results, or assessment output from app_assessment tool.";

    public ContainerizationAssessmentTool(ILogger<ContainerizationAssessmentTool> logger) : base(logger)
    {
        Parameters.Add(new ToolParameter("app_info", "Application description, framework version, dependencies, or results from app_assessment tool. Include details about file system usage, Windows-specific dependencies, stateful behavior, and scale requirements.", true));
    }

    public override async Task<string> ExecuteAsync(
        IDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var input = GetRequiredString(arguments, "app_info");
        Logger.LogInformation("Running containerization assessment ({Length} chars)", input.Length);

        // Detect patterns in the input to score readiness
        var lower = input.ToLowerInvariant();
        var blockers = new List<string>();
        var warnings = new List<string>();
        var score = 85; // Start optimistic

        // COM dependencies
        if (lower.Contains("com reference") || lower.Contains("com+") || lower.Contains("interop"))
        {
            blockers.Add("COM/COM+ dependencies require Windows containers — Linux containers will not work");
            score -= 25;
        }

        // GAC assemblies
        if (lower.Contains("gac") || lower.Contains("global assembly cache"))
        {
            blockers.Add("GAC assemblies must be included in the container image or replaced with NuGet packages");
            score -= 15;
        }

        // Windows Registry
        if (lower.Contains("registry") || lower.Contains("hklm") || lower.Contains("hkcu"))
        {
            blockers.Add("Windows Registry usage requires Windows containers and registry initialization on startup");
            score -= 20;
        }

        // Windows Services
        if (lower.Contains("windows service") || lower.Contains("topshelf"))
        {
            warnings.Add("Windows Services should be converted to background workers (IHostedService) for container hosting");
            score -= 10;
        }

        // File system state
        if (lower.Contains("local file") || lower.Contains("file system") || lower.Contains("temp file") ||
            lower.Contains("upload folder") || lower.Contains("shared folder"))
        {
            warnings.Add("File system state should be migrated to Azure Blob Storage or Azure Files mount");
            score -= 10;
        }

        // Local certificates
        if (lower.Contains("certificate store") || lower.Contains("local cert") || lower.Contains("x509"))
        {
            warnings.Add("Local certificate stores should be replaced with Azure Key Vault certificate references");
            score -= 10;
        }

        // WCF
        if (lower.Contains("wcf") || lower.Contains("system.servicemodel"))
        {
            warnings.Add("WCF requires CoreWCF migration or Windows containers with .NET Framework base image");
            score -= 15;
        }

        // MSMQ
        if (lower.Contains("msmq"))
        {
            blockers.Add("MSMQ is not available in containers — must migrate to Azure Service Bus before containerization");
            score -= 20;
        }

        // .NET Framework version detection
        bool isLegacyFramework = lower.Contains("net45") || lower.Contains("v4.5") || lower.Contains("net46") ||
            lower.Contains("v4.6") || lower.Contains("net47") || lower.Contains("v4.7") || lower.Contains("net48") ||
            lower.Contains("v4.8") || lower.Contains(".net framework") || lower.Contains("net40") || lower.Contains("v4.0");
        bool isModernDotNet = lower.Contains("net6") || lower.Contains("net7") || lower.Contains("net8") || lower.Contains("net9") ||
            lower.Contains(".net core") || lower.Contains("netcoreapp");

        string baseImage;
        string dockerfileTemplate;

        if (isLegacyFramework && !isModernDotNet)
        {
            baseImage = "mcr.microsoft.com/dotnet/framework/aspnet:4.8-windowsservercore-ltsc2022";
            warnings.Add("Legacy .NET Framework requires Windows containers — larger images, slower startup");
            score -= 10;
            dockerfileTemplate = GenerateWindowsDockerfile();
        }
        else
        {
            baseImage = "mcr.microsoft.com/dotnet/aspnet:9.0-alpine";
            dockerfileTemplate = GenerateLinuxDockerfile();
        }

        score = Math.Max(0, Math.Min(100, score));

        // Recommend container target
        var (target, targetReason) = RecommendContainerTarget(lower, score);

        return ToJson(new
        {
            success = true,
            tool = "containerization_assessment",
            readinessScore = score,
            readinessLevel = score >= 75 ? "Ready" : score >= 45 ? "Ready with modifications" : "Significant work required",
            blockers,
            warnings,
            containerStrategy = new
            {
                baseImage,
                containerType = isLegacyFramework && !isModernDotNet ? "Windows" : "Linux",
                recommendedTarget = target,
                targetReason,
                registryRecommendation = "Azure Container Registry (ACR) in Azure Gov — USGov Virginia or USGov Arizona"
            },
            dockerfile = score >= 30 ? dockerfileTemplate : null,
            dockerfileNote = score < 30 ? "Containerization readiness is too low — address blockers first" : "Template Dockerfile — customize paths and build args for your project",
            azureGovAvailability = new Dictionary<string, string>
            {
                ["Azure Container Registry"] = "Available in USGov Virginia, USGov Arizona",
                ["Azure Kubernetes Service (AKS)"] = "Available in USGov Virginia, USGov Arizona, USGov Texas",
                ["Azure Container Instances (ACI)"] = "Available in USGov Virginia, USGov Arizona",
                ["Azure Container Apps"] = "Available in USGov Virginia, USGov Arizona"
            },
            recommendations = new[]
            {
                "Build and test the container locally with Docker Desktop before deploying to Azure",
                "Use multi-stage builds to minimize image size",
                "Run containers as non-root user for security (SC-7)",
                "Scan container images with Microsoft Defender for Containers",
                "Store secrets in Azure Key Vault, not in environment variables or Dockerfiles",
                "Configure health check endpoints for orchestrator probes"
            },
            userInput = input
        });
    }

    private (string target, string reason) RecommendContainerTarget(string input, int readinessScore)
    {
        if (input.Contains("microservice") || input.Contains("multiple service") || input.Contains("scale"))
            return ("Azure Kubernetes Service (AKS)",
                "Best for multi-container microservices, advanced networking, auto-scaling, and team-managed infrastructure");

        if (input.Contains("event-driven") || input.Contains("auto-scale") || input.Contains("serverless container"))
            return ("Azure Container Apps",
                "Best for event-driven, auto-scaling containerized apps with built-in Dapr and KEDA support");

        if (input.Contains("simple") || input.Contains("single container") || input.Contains("batch") || input.Contains("job"))
            return ("Azure Container Instances (ACI)",
                "Best for simple single-container workloads, batch jobs, or dev/test — no cluster management");

        if (readinessScore >= 75)
            return ("Azure Container Apps",
                "Good default for most web apps — serverless scaling, built-in ingress, lower ops overhead than AKS");

        return ("Azure Kubernetes Service (AKS)",
            "Provides most flexibility for complex migration scenarios with Windows/Linux node pools");
    }

    private string GenerateLinuxDockerfile()
    {
        return @"# Multi-stage build for .NET application
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project files and restore
COPY [""YourApp/*.csproj"", ""YourApp/""]
RUN dotnet restore ""YourApp/YourApp.csproj""

# Copy source and build
COPY . .
WORKDIR /src/YourApp
RUN dotnet build -c Release -o /app/build
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS final
WORKDIR /app

# Security: run as non-root
RUN addgroup -S appgroup && adduser -S appuser -G appgroup
USER appuser

COPY --from=build /app/publish .

# Health check
HEALTHCHECK --interval=30s --timeout=10s --retries=3 \
  CMD wget -q --spider http://localhost:8080/health || exit 1

EXPOSE 8080
ENTRYPOINT [""dotnet"", ""YourApp.dll""]";
    }

    private string GenerateWindowsDockerfile()
    {
        return @"# Windows container for .NET Framework application
FROM mcr.microsoft.com/dotnet/framework/sdk:4.8-windowsservercore-ltsc2022 AS build
WORKDIR /src

# Copy solution and restore
COPY . .
RUN nuget restore YourApp.sln
RUN msbuild YourApp.sln /p:Configuration=Release /p:DeployOnBuild=true /p:PublishProfile=FolderProfile

# Runtime image
FROM mcr.microsoft.com/dotnet/framework/aspnet:4.8-windowsservercore-ltsc2022 AS final
WORKDIR /inetpub/wwwroot

COPY --from=build /src/YourApp/bin/Release/Publish .

# Health check
HEALTHCHECK --interval=30s --timeout=10s --retries=3 \
  CMD powershell -Command ""try { $response = Invoke-WebRequest -Uri http://localhost/health -UseBasicParsing; if ($response.StatusCode -eq 200) { exit 0 } } catch { exit 1 }""

EXPOSE 80";
    }
}
