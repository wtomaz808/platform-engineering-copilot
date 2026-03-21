using System.Net.Http.Json;
using Platform.Engineering.Copilot.Admin.Client.Models;

namespace Platform.Engineering.Copilot.Admin.Client.Services;

/// <summary>
/// API service for the Developer Portal — connections, repositories, work items, pipelines
/// </summary>
public class DevPortalApiService
{
    private readonly HttpClient _httpClient;
    private const string BaseUrl = "api/devportal";

    public DevPortalApiService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    #region Summary & Connections

    public async Task<DevPortalSummary?> GetSummaryAsync()
    {
        return await _httpClient.GetFromJsonAsync<DevPortalSummary>($"{BaseUrl}/summary");
    }

    public async Task<List<DevOpsConnection>> GetConnectionsAsync()
    {
        var result = await _httpClient.GetFromJsonAsync<List<DevOpsConnection>>($"{BaseUrl}/connections");
        return result ?? new List<DevOpsConnection>();
    }

    public async Task<DevOpsConnection?> CreateConnectionAsync(CreateConnectionRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync($"{BaseUrl}/connections", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DevOpsConnection>();
    }

    public async Task<ConnectionTestResult?> TestConnectionAsync(CreateConnectionRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync($"{BaseUrl}/connections/test", request);
        return await response.Content.ReadFromJsonAsync<ConnectionTestResult>();
    }

    public async Task DeleteConnectionAsync(string id)
    {
        var response = await _httpClient.DeleteAsync($"{BaseUrl}/connections/{id}");
        response.EnsureSuccessStatusCode();
    }

    public async Task<SyncResult?> SyncConnectionAsync(string id)
    {
        var response = await _httpClient.PostAsync($"{BaseUrl}/connections/{id}/sync", null);
        return await response.Content.ReadFromJsonAsync<SyncResult>();
    }

    #endregion

    #region Repositories

    public async Task<List<DevPortalRepository>> GetRepositoriesAsync(
        string? connectionId = null,
        string? provider = null)
    {
        var query = $"{BaseUrl}/repositories?";
        if (!string.IsNullOrEmpty(connectionId)) query += $"connectionId={Uri.EscapeDataString(connectionId)}&";
        if (!string.IsNullOrEmpty(provider)) query += $"provider={Uri.EscapeDataString(provider)}&";

        var result = await _httpClient.GetFromJsonAsync<List<DevPortalRepository>>(query.TrimEnd('&', '?'));
        return result ?? new List<DevPortalRepository>();
    }

    #endregion

    #region Work Items

    public async Task<List<DevPortalWorkItem>> GetWorkItemsAsync(
        string? connectionId = null,
        string? provider = null,
        string? repository = null,
        string? state = null)
    {
        var query = $"{BaseUrl}/workitems?";
        if (!string.IsNullOrEmpty(connectionId)) query += $"connectionId={Uri.EscapeDataString(connectionId)}&";
        if (!string.IsNullOrEmpty(provider)) query += $"provider={Uri.EscapeDataString(provider)}&";
        if (!string.IsNullOrEmpty(repository)) query += $"repository={Uri.EscapeDataString(repository)}&";
        if (!string.IsNullOrEmpty(state)) query += $"state={Uri.EscapeDataString(state)}&";

        var result = await _httpClient.GetFromJsonAsync<List<DevPortalWorkItem>>(query.TrimEnd('&', '?'));
        return result ?? new List<DevPortalWorkItem>();
    }

    #endregion

    #region Pipelines

    public async Task<List<DevPortalPipeline>> GetPipelinesAsync(
        string? connectionId = null,
        string? provider = null,
        string? repository = null)
    {
        var query = $"{BaseUrl}/pipelines?";
        if (!string.IsNullOrEmpty(connectionId)) query += $"connectionId={Uri.EscapeDataString(connectionId)}&";
        if (!string.IsNullOrEmpty(provider)) query += $"provider={Uri.EscapeDataString(provider)}&";
        if (!string.IsNullOrEmpty(repository)) query += $"repository={Uri.EscapeDataString(repository)}&";

        var result = await _httpClient.GetFromJsonAsync<List<DevPortalPipeline>>(query.TrimEnd('&', '?'));
        return result ?? new List<DevPortalPipeline>();
    }

    #endregion
}
