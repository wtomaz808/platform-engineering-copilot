using System.Net.Http.Json;
using Platform.Engineering.Copilot.Admin.Client.Models;

namespace Platform.Engineering.Copilot.Admin.Client.Services;

public class CostApiService
{
    private readonly HttpClient _httpClient;
    private const string BaseUrl = "api/cost";

    public CostApiService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<CostSummaryResponse?> GetCostSummaryAsync(bool forceRefresh = false)
    {
        try
        {
            var url = forceRefresh ? $"{BaseUrl}/summary?forceRefresh=true" : $"{BaseUrl}/summary";
            return await _httpClient.GetFromJsonAsync<CostSummaryResponse>(url);
        }
        catch
        {
            return null;
        }
    }

    public async Task<CostByResourceGroupResponse?> GetCostByResourceGroupAsync(bool forceRefresh = false)
    {
        try
        {
            var url = forceRefresh ? $"{BaseUrl}/by-resource-group?forceRefresh=true" : $"{BaseUrl}/by-resource-group";
            return await _httpClient.GetFromJsonAsync<CostByResourceGroupResponse>(url);
        }
        catch
        {
            return null;
        }
    }
}
