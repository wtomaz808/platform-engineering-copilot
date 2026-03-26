using System.Net.Http.Json;
using Platform.Engineering.Copilot.Admin.Client.Models;

namespace Platform.Engineering.Copilot.Admin.Client.Services;

public class AzureResourcesApiService
{
    private readonly HttpClient _httpClient;
    private const string BaseUrl = "api/azureresources";

    public AzureResourcesApiService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<AzureResourceOverview?> GetOverviewAsync()
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<AzureResourceOverview>($"{BaseUrl}/overview");
        }
        catch
        {
            return null;
        }
    }
}
