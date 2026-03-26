using System.Net.Http.Json;
using Platform.Engineering.Copilot.Admin.Client.Models;

namespace Platform.Engineering.Copilot.Admin.Client.Services;

public class DriftApiService
{
    private readonly HttpClient _httpClient;
    private const string BaseUrl = "api/drift";

    public DriftApiService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<DriftScanResponse?> ScanForDriftAsync(bool forceRefresh = false)
    {
        try
        {
            var url = forceRefresh ? $"{BaseUrl}/scan?forceRefresh=true" : $"{BaseUrl}/scan";
            return await _httpClient.GetFromJsonAsync<DriftScanResponse>(url);
        }
        catch
        {
            return null;
        }
    }
}
