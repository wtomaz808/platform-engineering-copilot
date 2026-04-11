param(
    [string]$SqlAdminPassword,
    [string]$OpenAiApiKey,
    [string]$SpClientSecret,
    [string]$AcrAdminPassword = '4DqpNEovJ6xcf2YITZeHYyIqf8HZQ0xdNhZre8OXevQwSpu8EAlrJQQJ99CDAA2z705Eqg7NAAACAZCRm81r',
    [string]$AcrAdminUser    = 'pecopdevacr'
)

$az = "C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin\az.cmd"

if (-not (Test-Path $az)) {
    Write-Error "az CLI not found at $az"
    exit 1
}

$deployName = "pecop-deploy-$(Get-Date -Format 'yyyyMMdd-HHmm')"
Write-Host "Starting deployment '$deployName' to Azure Gov (usgovarizona)..." -ForegroundColor Cyan

& $az deployment group create `
    --resource-group rg-pecopilot `
    --template-file infra/bicep/main.bicep `
    --parameters infra/bicep/main.azgov.bicepparam `
    --parameters "sqlAdminPassword=$SqlAdminPassword" `
    --parameters "openAiApiKey=$OpenAiApiKey" `
    --parameters "openAiEndpoint=https://usgovarizona.api.cognitive.microsoft.us/" `
    --parameters "openAiDeploymentName=gpt-4.1" `
    --parameters "spClientId=cd32b816-a2ba-4235-9082-229c51e015a5" `
    --parameters "spClientSecret=$SpClientSecret" `
    --parameters "spTenantId=3f6a68b1-d970-4905-863c-791674c10cf7" `
    --parameters "spSubscriptionId=78003893-6e88-4fa2-a8f0-315067f22e79" `
    --parameters "acrAdminUsername=$AcrAdminUser" `
    --parameters "acrAdminPassword=$AcrAdminPassword" `
    --name $deployName

if ($LASTEXITCODE -ne 0) {
    Write-Error "Deployment failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

Write-Host "Deployment completed successfully."
