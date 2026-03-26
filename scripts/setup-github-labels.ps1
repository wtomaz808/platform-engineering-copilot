# Platform Engineering Copilot - GitHub Labels Setup
# Run this script after setting $env:GITHUB_TOKEN
# Usage: .\setup-github-labels.ps1

param(
    [string]$Owner = "wtomaz808",
    [string]$Repo = "platform-engineering-copilot"
)

if (-not $env:GITHUB_TOKEN) {
    Write-Error "Set `$env:GITHUB_TOKEN` first (GitHub PAT with repo scope)"
    exit 1
}

$headers = @{
    "Authorization" = "Bearer $env:GITHUB_TOKEN"
    "Accept"        = "application/vnd.github+json"
    "X-GitHub-Api-Version" = "2022-11-28"
}
$baseUrl = "https://api.github.com/repos/$Owner/$Repo/labels"

$labels = @(
    # Area labels (blue family)
    @{ name = "area/admin-ui";     color = "1d76db"; description = "Admin Client (Blazor WASM)" }
    @{ name = "area/admin-api";    color = "1d76db"; description = "Admin API" }
    @{ name = "area/mcp-server";   color = "1d76db"; description = "MCP Server" }
    @{ name = "area/chat";         color = "1d76db"; description = "Chat UI" }
    @{ name = "area/agents";       color = "1d76db"; description = "AI Agents & Tools" }
    @{ name = "area/infra";        color = "1d76db"; description = "Bicep / Terraform / Kubernetes" }
    @{ name = "area/compliance";   color = "1d76db"; description = "Compliance scanning & policy" }
    @{ name = "area/devportal";    color = "1d76db"; description = "Developer Portal (ADO/GitHub)" }
    @{ name = "area/database";     color = "1d76db"; description = "EF Core / SQL Server / State" }

    # Type labels (keep existing bug/enhancement, add these)
    @{ name = "type/feature";      color = "0e8a16"; description = "New feature spec" }
    @{ name = "type/bug";          color = "d73a4a"; description = "Bug report" }
    @{ name = "type/enhancement";  color = "a2eeef"; description = "Improvement to existing feature" }
    @{ name = "type/chore";        color = "fef2c0"; description = "Maintenance / refactor / cleanup" }

    # Priority labels
    @{ name = "priority/critical"; color = "b60205"; description = "Must fix immediately" }
    @{ name = "priority/high";     color = "d93f0b"; description = "Next sprint" }
    @{ name = "priority/medium";   color = "fbca04"; description = "Soon" }
    @{ name = "priority/low";      color = "c2e0c6"; description = "Nice to have" }

    # Workflow labels
    @{ name = "needs-triage";              color = "ededed"; description = "Awaiting initial review" }
    @{ name = "spec-approved";             color = "0e8a16"; description = "Spec reviewed and approved" }
    @{ name = "constitution-amendment";    color = "d876e3"; description = "Proposes a change to CONSTITUTION.md" }
)

foreach ($label in $labels) {
    $body = $label | ConvertTo-Json
    try {
        $response = Invoke-RestMethod -Uri $baseUrl -Method Post -Headers $headers -Body $body -ContentType "application/json" -ErrorAction Stop
        Write-Host "  Created: $($label.name)" -ForegroundColor Green
    }
    catch {
        $status = $_.Exception.Response.StatusCode.value__
        if ($status -eq 422) {
            # Label already exists - update it
            $encodedName = [Uri]::EscapeDataString($label.name)
            try {
                Invoke-RestMethod -Uri "$baseUrl/$encodedName" -Method Patch -Headers $headers -Body $body -ContentType "application/json" -ErrorAction Stop | Out-Null
                Write-Host "  Updated: $($label.name)" -ForegroundColor Yellow
            }
            catch {
                Write-Host "  Failed to update: $($label.name) - $_" -ForegroundColor Red
            }
        }
        else {
            Write-Host "  Failed: $($label.name) - $_" -ForegroundColor Red
        }
    }
}

Write-Host "`nDone. $($labels.Count) labels processed." -ForegroundColor Cyan
