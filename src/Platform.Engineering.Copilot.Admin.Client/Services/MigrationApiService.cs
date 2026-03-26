using System.Net.Http.Json;
using Platform.Engineering.Copilot.Admin.Client.Models;

namespace Platform.Engineering.Copilot.Admin.Client.Services;

public class MigrationApiService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MigrationApiService> _logger;

    public MigrationApiService(HttpClient httpClient, ILogger<MigrationApiService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<MigrationDashboard?> GetDashboardAsync()
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<MigrationDashboard>("api/migration/dashboard");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get migration dashboard");
            throw;
        }
    }

    public async Task<MigrationAssessment?> RunAssessmentAsync(MigrationAssessmentRequest request)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("api/migration/assess", request);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<MigrationAssessment>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to run migration assessment");
            throw;
        }
    }

    public async Task<TargetRecommendationsResponse?> GetTargetRecommendationsAsync(MigrationAssessmentRequest request)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("api/migration/recommendations", request);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<TargetRecommendationsResponse>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get target recommendations");
            throw;
        }
    }

    public async Task<List<MigrationAssessment>> GetAssessmentsAsync()
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<List<MigrationAssessment>>("api/migration/assessments") ?? new();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get assessments");
            throw;
        }
    }

    public async Task DeleteAssessmentAsync(string id)
    {
        try
        {
            var response = await _httpClient.DeleteAsync($"api/migration/assessments/{id}");
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete assessment {Id}", id);
            throw;
        }
    }
}
