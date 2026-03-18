using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Platform.Engineering.Copilot.Agents.DevOps.Configuration;
using Platform.Engineering.Copilot.Chat.App.Models;
using Platform.Engineering.Copilot.Chat.App.Services;
using Platform.Engineering.Copilot.Core.Configuration;

namespace Platform.Engineering.Copilot.Chat.App.Controllers;

/// <summary>
/// API controller for chat conversations
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ConversationsController : ControllerBase
{
    private readonly IChatService _chatService;
    private readonly ILogger<ConversationsController> _logger;

    public ConversationsController(IChatService chatService, ILogger<ConversationsController> logger)
    {
        _chatService = chatService;
        _logger = logger;
    }

    /// <summary>
    /// Get all conversations for a user
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<Conversation>>> GetConversations(
        [FromQuery] string userId = "default-user",
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50)
    {
        try
        {
            var conversations = await _chatService.GetConversationsAsync(userId, skip, take);
            return Ok(conversations);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting conversations for user {UserId}", userId);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Get a specific conversation
    /// </summary>
    [HttpGet("{conversationId}")]
    public async Task<ActionResult<Conversation>> GetConversation(string conversationId)
    {
        try
        {
            var conversation = await _chatService.GetConversationAsync(conversationId);
            if (conversation == null)
                return NotFound();

            return Ok(conversation);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting conversation {ConversationId}", conversationId);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Create a new conversation
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<Conversation>> CreateConversation(
        [FromBody] CreateConversationRequest request)
    {
        try
        {
            var conversation = await _chatService.CreateConversationAsync(
                request.Title ?? "New Conversation",
                request.UserId ?? "default-user");

            return CreatedAtAction(nameof(GetConversation), new { conversationId = conversation.Id }, conversation);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating conversation");
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Rename a conversation
    /// </summary>
    [HttpPatch("{conversationId}/title")]
    public async Task<ActionResult<Conversation>> UpdateConversationTitle(
        string conversationId,
        [FromBody] UpdateConversationTitleRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Title))
                return BadRequest("Title is required");

            var conversation = await _chatService.UpdateConversationTitleAsync(conversationId, request.Title);
            if (conversation == null)
                return NotFound();

            return Ok(conversation);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error renaming conversation {ConversationId}", conversationId);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Delete a conversation
    /// </summary>
    [HttpDelete("{conversationId}")]
    public async Task<ActionResult> DeleteConversation(string conversationId)
    {
        try
        {
            var success = await _chatService.DeleteConversationAsync(conversationId);
            if (!success)
                return NotFound();

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting conversation {ConversationId}", conversationId);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Search conversations
    /// </summary>
    [HttpGet("search")]
    public async Task<ActionResult<List<Conversation>>> SearchConversations(
        [FromQuery] string query,
        [FromQuery] string userId = "default-user")
    {
        try
        {
            if (string.IsNullOrWhiteSpace(query))
                return BadRequest("Search query is required");

            var conversations = await _chatService.SearchConversationsAsync(query, userId);
            return Ok(conversations);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching conversations with query: {Query}", query);
            return StatusCode(500, "Internal server error");
        }
    }
}

/// <summary>
/// API controller for chat messages
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class MessagesController : ControllerBase
{
    private readonly IChatService _chatService;
    private readonly ILogger<MessagesController> _logger;

    public MessagesController(IChatService chatService, ILogger<MessagesController> logger)
    {
        _chatService = chatService;
        _logger = logger;
    }

    /// <summary>
    /// Get messages for a conversation
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<ChatMessage>>> GetMessages(
        [FromQuery] string conversationId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(conversationId))
                return BadRequest("ConversationId is required");

            var messages = await _chatService.GetMessagesAsync(conversationId, skip, take);
            return Ok(messages);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting messages for conversation {ConversationId}", conversationId);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Send a message
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ChatMessage>> SendMessage([FromBody] ChatRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Message))
                return BadRequest("Message content is required");

            var response = await _chatService.SendMessageAsync(request);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message");
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Upload an attachment
    /// </summary>
    [HttpPost("{messageId}/attachments")]
    public async Task<ActionResult<MessageAttachment>> UploadAttachment(
        string messageId,
        IFormFile file)
    {
        try
        {
            if (file == null || file.Length == 0)
                return BadRequest("File is required");

            if (file.Length > 10 * 1024 * 1024) // 10MB limit
                return BadRequest("File size too large (max 10MB)");

            var attachment = await _chatService.UploadAttachmentAsync(messageId, file);
            return Ok(attachment);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading attachment for message {MessageId}", messageId);
            return StatusCode(500, "Internal server error");
        }
    }
}

/// <summary>
/// Request model for creating conversations
/// </summary>
public class CreateConversationRequest
{
    public string? Title { get; set; }
    public string? UserId { get; set; }
}

/// <summary>
/// Request model for renaming a conversation
/// </summary>
public class UpdateConversationTitleRequest
{
    public string Title { get; set; } = string.Empty;
}

/// <summary>
/// API controller for runtime settings overrides — proxies to the MCP server.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SettingsController : ControllerBase
{
    private readonly ILogger<SettingsController> _logger;

    public SettingsController(ILogger<SettingsController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Override GitHub settings at runtime — proxies to the MCP server where agents actually run.
    /// </summary>
    [HttpPost("github")]
    public async Task<IActionResult> UpdateGitHubSettings(
        [FromBody] GitHubSettingsRequest request,
        [FromServices] IHttpClientFactory httpClientFactory,
        [FromServices] IConfiguration configuration)
    {
        var mcpBaseUrl = configuration["McpServer:BaseUrl"] ?? "http://platform-mcp:5100";
        try
        {
            using var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            var resp = await client.PostAsJsonAsync($"{mcpBaseUrl}/settings/github", request);
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("MCP settings proxy returned {Status}: {Body}", resp.StatusCode, body);
                return StatusCode((int)resp.StatusCode, body);
            }
            _logger.LogInformation("GitHub settings forwarded to MCP. Org: {Org}", request.Organization);
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to forward GitHub settings to MCP server at {Url}", mcpBaseUrl);
            return StatusCode(502, new { error = "Could not reach MCP server", detail = ex.Message });
        }
    }

    /// <summary>
    /// Test GitHub connectivity by calling the MCP debug endpoint.
    /// </summary>
    [HttpGet("github/test")]
    public async Task<IActionResult> TestGitHubConnection(
        [FromQuery] string? org,
        [FromQuery] string? token,
        [FromServices] IHttpClientFactory httpClientFactory,
        [FromServices] IConfiguration configuration)
    {
        var mcpBaseUrl = configuration["McpServer:BaseUrl"] ?? "http://platform-mcp:5100";
        try
        {
            // Push the token to MCP first so the test uses the user-supplied token
            if (!string.IsNullOrWhiteSpace(token))
            {
                using var settingsClient = httpClientFactory.CreateClient();
                settingsClient.Timeout = TimeSpan.FromSeconds(10);
                await settingsClient.PostAsJsonAsync($"{mcpBaseUrl}/settings/github",
                    new { Organization = org, AccessToken = token });
            }

            using var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15);
            var url = string.IsNullOrWhiteSpace(org)
                ? $"{mcpBaseUrl}/mcp/debug/github/repos"
                : $"{mcpBaseUrl}/mcp/debug/github/repos?org={Uri.EscapeDataString(org)}";
            var resp = await client.GetAsync(url);
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
                return StatusCode((int)resp.StatusCode, body);
            return Content(body, "application/json");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GitHub connection test failed");
            return StatusCode(502, new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// Save Azure DevOps settings (currently stored client-side; endpoint is reserved for future persistence).
    /// </summary>
    [HttpPost("ado")]
    public IActionResult UpdateAdoSettings([FromBody] AdoSettingsRequest request)
    {
        _logger.LogInformation("ADO settings update received. Org: {Org}", request.ServerUrl);
        return Ok(new { success = true });
    }

    /// <summary>
    /// Test Azure DevOps connectivity using the provided PAT.
    /// </summary>
    [HttpGet("ado/test")]
    public async Task<IActionResult> TestAdoConnection(
        [FromQuery] string? serverUrl,
        [FromQuery] string? token,
        [FromServices] IHttpClientFactory httpClientFactory)
    {
        if (string.IsNullOrWhiteSpace(serverUrl))
            return BadRequest(new { success = false, error = "serverUrl is required" });

        try
        {
            using var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15);

            // Normalise URL — ensure it ends without trailing slash, then append projects API
            var baseUrl = serverUrl.TrimEnd('/');
            var apiUrl = $"{baseUrl}/_apis/projects?api-version=7.0&$top=1";

            if (!string.IsNullOrWhiteSpace(token))
            {
                var encoded = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($":{token}"));
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", encoded);
            }

            var resp = await client.GetAsync(apiUrl);
            if (resp.IsSuccessStatusCode)
            {
                return Ok(new { success = true, message = "Connected to Azure DevOps successfully" });
            }

            var body = await resp.Content.ReadAsStringAsync();
            _logger.LogWarning("ADO test returned {Status}: {Body}", resp.StatusCode, body);
            return StatusCode((int)resp.StatusCode, new { success = false, error = $"ADO returned {(int)resp.StatusCode}" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ADO connection test failed for {Url}", serverUrl);
            return StatusCode(502, new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// Acknowledge OpenAI settings from the UI (actual keys must be set via environment variables).
    /// </summary>
    [HttpPost("openai")]
    public IActionResult UpdateOpenAISettings([FromBody] OpenAISettingsRequest request)
    {
        _logger.LogInformation("OpenAI settings acknowledged from UI. Endpoint: {Endpoint}", request.Endpoint);
        return Ok(new { success = true, note = "Settings stored in browser. Restart required for backend key changes." });
    }
}

/// <summary>Request model for updating GitHub settings</summary>
public class GitHubSettingsRequest
{
    public string? Organization { get; set; }
    public string? AccessToken { get; set; }
}

/// <summary>Request model for updating Azure DevOps settings</summary>
public class AdoSettingsRequest
{
    public string? ServerUrl { get; set; }
    public string? PortalUrl { get; set; }
    public string? AccessToken { get; set; }
}

/// <summary>Request model for updating OpenAI settings</summary>
public class OpenAISettingsRequest
{
    public string? ApiKey { get; set; }
    public string? Endpoint { get; set; }
    public string? ChatDeployment { get; set; }
    public string? EmbeddingDeployment { get; set; }
}