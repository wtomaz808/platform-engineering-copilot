using System.Net.Http.Json;
using Platform.Engineering.Copilot.Admin.Client.Models;

namespace Platform.Engineering.Copilot.Admin.Client.Services;

public class IntegrationsApiService
{
    private readonly HttpClient _httpClient;
    private const string BaseUrl = "api/integrations";

    public IntegrationsApiService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IntegrationSettings?> GetIntegrationsAsync()
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<IntegrationSettings>(BaseUrl);
        }
        catch
        {
            return null;
        }
    }

    public async Task SaveIntegrationsAsync(IntegrationSettings settings)
    {
        var response = await _httpClient.PostAsJsonAsync(BaseUrl, settings);
        response.EnsureSuccessStatusCode();
    }

    public async Task<ConnectionTestResult?> TestAzureConnectionAsync(AzureIntegrationSettings settings)
    {
        var response = await _httpClient.PostAsJsonAsync($"{BaseUrl}/azure/test", settings);
        return await response.Content.ReadFromJsonAsync<ConnectionTestResult>();
    }

    public async Task<ConnectionTestResult?> TestGitHubConnectionAsync(GitHubIntegrationSettings settings)
    {
        var response = await _httpClient.PostAsJsonAsync($"{BaseUrl}/github/test", settings);
        return await response.Content.ReadFromJsonAsync<ConnectionTestResult>();
    }

    public async Task<ConnectionTestResult?> TestAdoConnectionAsync(AdoIntegrationSettings settings)
    {
        var response = await _httpClient.PostAsJsonAsync($"{BaseUrl}/ado/test", settings);
        return await response.Content.ReadFromJsonAsync<ConnectionTestResult>();
    }

    public async Task<List<string>?> GetAdoProjectsAsync(AdoIntegrationSettings settings)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync($"{BaseUrl}/ado/projects", settings);
            return await response.Content.ReadFromJsonAsync<List<string>>();
        }
        catch
        {
            return null;
        }
    }
}
