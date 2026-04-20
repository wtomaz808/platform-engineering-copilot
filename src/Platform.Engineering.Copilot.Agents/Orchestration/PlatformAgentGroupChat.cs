using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Platform.Engineering.Copilot.Agents.Common;
using Platform.Engineering.Copilot.Channels.Abstractions;
using Platform.Engineering.Copilot.State.Abstractions;
using Platform.Engineering.Copilot.State.Models;
using System.Runtime.CompilerServices;
using System.Text;

namespace Platform.Engineering.Copilot.Agents.Orchestration;

/// <summary>
/// Multi-agent orchestration using Agent FX patterns.
/// Coordinates multiple specialized agents to process user requests.
/// </summary>
public class PlatformAgentGroupChat
{
    private readonly Dictionary<string, BaseAgent> _agents = new();
    private readonly PlatformSelectionStrategy _selectionStrategy;
    private readonly PlatformTerminationStrategy _terminationStrategy;
    private readonly IChatClient _chatClient;
    private readonly IConversationStateManager _conversationStateManager;
    private readonly IChannelManager _channelManager;
    private readonly IStreamingHandler _streamingHandler;
    private readonly ILogger<PlatformAgentGroupChat> _logger;

    public int MaxIterations { get; set; } = 10;

    public PlatformAgentGroupChat(
        IEnumerable<BaseAgent> agents,
        PlatformSelectionStrategy selectionStrategy,
        PlatformTerminationStrategy terminationStrategy,
        IChatClient chatClient,
        IConversationStateManager conversationStateManager,
        IChannelManager channelManager,
        IStreamingHandler streamingHandler,
        ILogger<PlatformAgentGroupChat> logger)
    {
        _selectionStrategy = selectionStrategy ?? throw new ArgumentNullException(nameof(selectionStrategy));
        _terminationStrategy = terminationStrategy ?? throw new ArgumentNullException(nameof(terminationStrategy));
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _conversationStateManager = conversationStateManager ?? throw new ArgumentNullException(nameof(conversationStateManager));
        _channelManager = channelManager ?? throw new ArgumentNullException(nameof(channelManager));
        _streamingHandler = streamingHandler ?? throw new ArgumentNullException(nameof(streamingHandler));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        foreach (var agent in agents)
        {
            _agents[agent.AgentName] = agent;
            _logger.LogInformation("Registered agent: {AgentName}", agent.AgentName);
        }

        _logger.LogInformation("🎼 PlatformAgentGroupChat initialized with {Count} agents", _agents.Count);
    }

    /// <summary>
    /// Process a user message through the multi-agent system
    /// </summary>
    public async IAsyncEnumerable<AgentResponse> ProcessAsync(
        string userMessage,
        AgentConversationContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("🎼 Processing: {Message}", userMessage);

        // Load/create conversation state
        var conversationState = await _conversationStateManager.GetOrCreateAsync(
            context.ConversationId, cancellationToken);
        
        // Store user message in conversation state
        await _conversationStateManager.AddMessageAsync(
            context.ConversationId,
            new ConversationMessage
            {
                Role = MessageRole.User,
                Content = userMessage,
                Timestamp = DateTime.UtcNow
            },
            cancellationToken);

        // Send "thinking" notification to channel
        await _channelManager.SendToConversationAsync(
            context.ConversationId,
            new ChannelMessage
            {
                ConversationId = context.ConversationId,
                Type = MessageType.AgentThinking,
                Content = "Analyzing your request..."
            },
            cancellationToken);

        var iteration = 0;
        var responses = new List<AgentResponse>();

        while (iteration < MaxIterations)
        {
            iteration++;
            _logger.LogDebug("Iteration {Iteration}/{MaxIterations}", iteration, MaxIterations);

            // Select agent
            var selectedAgent = await _selectionStrategy.SelectAgentAsync(
                _agents.Values.ToList(),
                userMessage,
                context,
                cancellationToken);

            if (selectedAgent == null)
            {
                _logger.LogWarning("No agent selected, ending orchestration");
                break;
            }

            _logger.LogInformation("🤖 Selected agent: {AgentName}", selectedAgent.AgentName);

            // Update conversation state with active agent
            conversationState.ActiveAgentType = selectedAgent.AgentName;
            await _conversationStateManager.SaveAsync(conversationState, cancellationToken);

            // Send progress notification
            await _channelManager.SendToConversationAsync(
                context.ConversationId,
                new ChannelMessage
                {
                    ConversationId = context.ConversationId,
                    Type = MessageType.ProgressUpdate,
                    Content = $"Routing to {selectedAgent.AgentName}...",
                    AgentType = selectedAgent.AgentName
                },
                cancellationToken);

            // Add user message to context for agent processing
            context.AddMessage(userMessage, true);

            // Process with selected agent
            var response = await selectedAgent.ProcessAsync(context, cancellationToken);
            responses.Add(response);

            // Add to context
            context.PreviousResponses.Add(response);
            context.AddMessage(response.Content, false, response.AgentName);

            // Store assistant message in conversation state
            await _conversationStateManager.AddMessageAsync(
                context.ConversationId,
                new ConversationMessage
                {
                    Role = MessageRole.Assistant,
                    Content = response.Content,
                    AgentType = response.AgentName,
                    Timestamp = DateTime.UtcNow
                },
                cancellationToken);

            // Send response through channel
            await _channelManager.SendToConversationAsync(
                context.ConversationId,
                new ChannelMessage
                {
                    ConversationId = context.ConversationId,
                    Type = MessageType.AgentResponse,
                    Content = response.Content,
                    AgentType = response.AgentName,
                    IsComplete = !response.RequiresHandoff
                },
                cancellationToken);

            yield return response;

            // Check for handoff
            if (response.RequiresHandoff && !string.IsNullOrEmpty(response.HandoffTarget))
            {
                _logger.LogInformation("🔄 Handoff to: {Target} - Reason: {Reason}",
                    response.HandoffTarget, response.HandoffReason);

                // Update message for handoff
                userMessage = $"[Handoff from {response.AgentName}]: {response.HandoffReason}\n\nOriginal request: {userMessage}";
                continue;
            }

            // Check termination
            if (await _terminationStrategy.ShouldTerminateAsync(responses, context, cancellationToken))
            {
                _logger.LogInformation("✅ Termination condition met");
                break;
            }

            // If no handoff needed, we're done
            break;
        }

        _logger.LogInformation("🏁 Orchestration complete after {Iterations} iterations", iteration);
    }

    /// <summary>
    /// Process and aggregate all responses into a single string
    /// </summary>
    public async Task<string> ProcessAndAggregateAsync(
        string userMessage,
        AgentConversationContext context,
        CancellationToken cancellationToken = default)
    {
        var result = new StringBuilder();

        await foreach (var response in ProcessAsync(userMessage, context, cancellationToken))
        {
            if (result.Length > 0)
            {
                result.AppendLine();
                result.AppendLine("---");
                result.AppendLine();
            }

            result.Append(response.Content);
        }

        return result.ToString();
    }

    /// <summary>
    /// Stream the response token-by-token for the selected agent.
    /// Runs agent selection and any tool-call rounds first, then streams the final answer.
    /// </summary>
    public async IAsyncEnumerable<string> ProcessStreamingAsync(
        string userMessage,
        AgentConversationContext context,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("🎼 Streaming: {Message}", userMessage);

        // Load/create conversation state and store user message (same as non-streaming path)
        var conversationState = await _conversationStateManager.GetOrCreateAsync(context.ConversationId, cancellationToken);
        await _conversationStateManager.AddMessageAsync(
            context.ConversationId,
            new ConversationMessage { Role = MessageRole.User, Content = userMessage, Timestamp = DateTime.UtcNow },
            cancellationToken);

        await _channelManager.SendToConversationAsync(
            context.ConversationId,
            new ChannelMessage { ConversationId = context.ConversationId, Type = MessageType.AgentThinking, Content = "Analyzing your request..." },
            cancellationToken);

        // Select agent (may make an LLM call for complex queries)
        context.AddMessage(userMessage, true);
        var selectedAgent = await _selectionStrategy.SelectAgentAsync(_agents.Values.ToList(), userMessage, context, cancellationToken);
        if (selectedAgent == null)
        {
            _logger.LogWarning("No agent selected for streaming");
            yield break;
        }

        _logger.LogInformation("🤖 Streaming with agent: {AgentName}", selectedAgent.AgentName);

        conversationState.ActiveAgentType = selectedAgent.AgentName;
        await _conversationStateManager.SaveAsync(conversationState, cancellationToken);

        await _channelManager.SendToConversationAsync(
            context.ConversationId,
            new ChannelMessage { ConversationId = context.ConversationId, Type = MessageType.ProgressUpdate, Content = $"Routing to {selectedAgent.AgentName}...", AgentType = selectedAgent.AgentName },
            cancellationToken);

        // Stream the agent's response token-by-token
        var fullResponse = new StringBuilder();
        await foreach (var chunk in selectedAgent.ProcessStreamingAsync(context, cancellationToken))
        {
            fullResponse.Append(chunk);
            yield return chunk;
        }

        // Persist the assistant message and update state after streaming
        await _conversationStateManager.AddMessageAsync(
            context.ConversationId,
            new ConversationMessage { Role = MessageRole.Assistant, Content = fullResponse.ToString(), AgentType = selectedAgent.AgentName, Timestamp = DateTime.UtcNow },
            cancellationToken);
    }

    /// <summary>
    /// Get a specific agent by name
    /// </summary>
    public BaseAgent? GetAgent(string name)
    {
        return _agents.TryGetValue(name, out var agent) ? agent : null;
    }

    /// <summary>
    /// Get all registered agents
    /// </summary>
    public IEnumerable<BaseAgent> GetAllAgents()
    {
        return _agents.Values;
    }
}
