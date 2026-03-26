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

    #region Summary

    public async Task<DevPortalSummary?> GetSummaryAsync()
    {
        return await _httpClient.GetFromJsonAsync<DevPortalSummary>($"{BaseUrl}/summary");
    }

    #endregion

    #region Repositories

    public async Task<List<DevPortalRepository>> GetRepositoriesAsync(
        string? provider = null)
    {
        var query = $"{BaseUrl}/repositories?";
        if (!string.IsNullOrEmpty(provider)) query += $"provider={Uri.EscapeDataString(provider)}&";

        var result = await _httpClient.GetFromJsonAsync<List<DevPortalRepository>>(query.TrimEnd('&', '?'));
        return result ?? new List<DevPortalRepository>();
    }

    #endregion

    #region Work Items

    public async Task<List<DevPortalWorkItem>> GetWorkItemsAsync(
        string? provider = null,
        string? repository = null,
        string? state = null)
    {
        var query = $"{BaseUrl}/workitems?";
        if (!string.IsNullOrEmpty(provider)) query += $"provider={Uri.EscapeDataString(provider)}&";
        if (!string.IsNullOrEmpty(repository)) query += $"repository={Uri.EscapeDataString(repository)}&";
        if (!string.IsNullOrEmpty(state)) query += $"state={Uri.EscapeDataString(state)}&";

        var result = await _httpClient.GetFromJsonAsync<List<DevPortalWorkItem>>(query.TrimEnd('&', '?'));
        return result ?? new List<DevPortalWorkItem>();
    }

    #endregion

    #region Pipelines

    public async Task<List<DevPortalPipeline>> GetPipelinesAsync(
        string? provider = null,
        string? repository = null)
    {
        var query = $"{BaseUrl}/pipelines?";
        if (!string.IsNullOrEmpty(provider)) query += $"provider={Uri.EscapeDataString(provider)}&";
        if (!string.IsNullOrEmpty(repository)) query += $"repository={Uri.EscapeDataString(repository)}&";

        var result = await _httpClient.GetFromJsonAsync<List<DevPortalPipeline>>(query.TrimEnd('&', '?'));
        return result ?? new List<DevPortalPipeline>();
    }

    #endregion
}
