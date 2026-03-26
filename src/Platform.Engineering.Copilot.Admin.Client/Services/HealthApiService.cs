using System.Net.Http.Json;
using Platform.Engineering.Copilot.Admin.Client.Models;

namespace Platform.Engineering.Copilot.Admin.Client.Services;

public class HealthApiService
{
    private readonly HttpClient _httpClient;
    private const string BaseUrl = "api/health";

    public HealthApiService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ResourceHealthResponse?> GetResourceHealthAsync()
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<ResourceHealthResponse>($"{BaseUrl}/resources");
        }
        catch
        {
            return null;
        }
    }

    public async Task<ServiceHealthResponse?> GetServiceHealthAlertsAsync()
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<ServiceHealthResponse>($"{BaseUrl}/alerts");
        }
        catch
        {
            return null;
        }
    }
}
