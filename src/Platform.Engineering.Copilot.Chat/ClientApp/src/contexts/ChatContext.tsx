import React, { createContext, useContext, useReducer, useEffect, useCallback, ReactNode } from 'react';
import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { chatApi } from '../services/chatApi';
import { Conversation, ChatMessage, ChatRequest } from '../types/chat';

console.log('📦 ChatContext module loaded at:', new Date().toISOString());
console.log('📦 SignalR imports:', { HubConnection, HubConnectionBuilder, LogLevel });

interface ChatState {
  conversations: Conversation[];
  currentConversation: Conversation | null;
  messages: ChatMessage[];
  isConnected: boolean;
  isLoading: boolean;
  error: string | null;
}

type ChatAction =
  | { type: 'SET_CONVERSATIONS'; payload: Conversation[] }
  | { type: 'SET_CURRENT_CONVERSATION'; payload: Conversation | null }
  | { type: 'SET_MESSAGES'; payload: ChatMessage[] }
  | { type: 'ADD_MESSAGE'; payload: ChatMessage }
  | { type: 'UPDATE_MESSAGE'; payload: ChatMessage }
  | { type: 'REMOVE_PROCESSING'; payload: string }  // conversationId
  | { type: 'START_STREAM'; payload: { messageId: string; conversationId: string } }
  | { type: 'APPEND_STREAM_CHUNK'; payload: { messageId: string; text: string } }
  | { type: 'UPSERT_MESSAGE'; payload: ChatMessage }
  | { type: 'SET_CONNECTED'; payload: boolean }
  | { type: 'SET_LOADING'; payload: boolean }
  | { type: 'SET_ERROR'; payload: string | null }
  | { type: 'ADD_CONVERSATION'; payload: Conversation }
  | { type: 'RENAME_CONVERSATION'; payload: { id: string; title: string } };

const initialState: ChatState = {
  conversations: [],
  currentConversation: null,
  messages: [],
  isConnected: false,
  isLoading: false,
  error: null,
};

const chatReducer = (state: ChatState, action: ChatAction): ChatState => {
  switch (action.type) {
    case 'SET_CONVERSATIONS':
      return { ...state, conversations: action.payload };
    case 'SET_CURRENT_CONVERSATION':
      return { ...state, currentConversation: action.payload };
    case 'SET_MESSAGES':
      return { ...state, messages: action.payload };
    case 'ADD_MESSAGE':
      return { ...state, messages: [...state.messages, action.payload] };
    case 'REMOVE_PROCESSING':
      return {
        ...state,
        messages: state.messages.filter(
          m => !(m.conversationId === action.payload && (m.status as string) === 'Processing')
        ),
      };
    case 'START_STREAM': {
      // Remove any existing 'Processing' placeholder and add a new streaming message
      const filtered = state.messages.filter(
        m => !(m.conversationId === action.payload.conversationId && (m.status as string) === 'Processing')
      );
      const streamMsg: ChatMessage = {
        id: action.payload.messageId,
        conversationId: action.payload.conversationId,
        content: '',
        role: 'Assistant' as any,
        timestamp: new Date().toISOString(),
        status: 'Streaming' as any,
        attachments: [],
        tools: [],
      };
      return { ...state, messages: [...filtered, streamMsg] };
    }
    case 'APPEND_STREAM_CHUNK':
      return {
        ...state,
        messages: state.messages.map(msg =>
          msg.id === action.payload.messageId
            ? { ...msg, content: msg.content + action.payload.text }
            : msg
        ),
      };
    case 'UPSERT_MESSAGE': {
      const idx = state.messages.findIndex(m => m.id === action.payload.id);
      if (idx >= 0) {
        const updated = [...state.messages];
        updated[idx] = action.payload;
        return { ...state, messages: updated };
      }
      return { ...state, messages: [...state.messages, action.payload] };
    }
    case 'UPDATE_MESSAGE':
      return {
        ...state,
        messages: state.messages.map(msg =>
          msg.id === action.payload.id ? action.payload : msg
        ),
      };
    case 'SET_CONNECTED':
      return { ...state, isConnected: action.payload };
    case 'SET_LOADING':
      return { ...state, isLoading: action.payload };
    case 'SET_ERROR':
      return { ...state, error: action.payload };
    case 'ADD_CONVERSATION':
      return { ...state, conversations: [action.payload, ...state.conversations] };
    case 'RENAME_CONVERSATION':
      return {
        ...state,
        conversations: state.conversations.map(c =>
          c.id === action.payload.id ? { ...c, title: action.payload.title } : c
        ),
        currentConversation:
          state.currentConversation?.id === action.payload.id
            ? { ...state.currentConversation, title: action.payload.title }
            : state.currentConversation,
      };
    default:
      return state;
  }
};

interface ChatContextType {
  state: ChatState;
  loadConversations: () => Promise<void>;
  selectConversation: (conversation: Conversation) => Promise<void>;
  createConversation: (title?: string) => Promise<Conversation>;
  sendMessage: (request: ChatRequest) => Promise<void>;
  deleteConversation: (conversationId: string) => Promise<void>;
  searchConversations: (query: string) => Promise<Conversation[]>;
  renameConversation: (conversationId: string, title: string) => Promise<void>;
}

const ChatContext = createContext<ChatContextType | undefined>(undefined);

export const ChatProvider: React.FC<{ children: ReactNode }> = ({ children }) => {
  const [state, dispatch] = useReducer(chatReducer, initialState);
  const [connection, setConnection] = React.useState<HubConnection | null>(null);
  const pendingModelRef = React.useRef<Map<string, string>>(new Map());

  useEffect(() => {
    console.log('🔄 ChatContext: Initializing SignalR connection...');
    
    let connection: HubConnection | null = null;
    
    const initializeSignalR = async () => {
      try {
        // Use current window location to build SignalR URL (works in both dev and production)
        const hubUrl = `${window.location.protocol}//${window.location.host}/chathub`;
        console.log('🔄 Creating SignalR connection to:', hubUrl);
        
        connection = new HubConnectionBuilder()
          .withUrl(hubUrl)
          .withAutomaticReconnect()
          .configureLogging(LogLevel.Warning)  // Only show warnings and errors
          .build();

        setConnection(connection);

        // Set up event handlers before starting
        connection.on('MessageReceived', (message: ChatMessage) => {
          console.log('📨 Received message via SignalR:', message);
          // Remove any "Processing your request..." placeholder
          dispatch({ type: 'REMOVE_PROCESSING', payload: message.conversationId });
          // Attach model attribution if tracked
          const model = pendingModelRef.current.get(message.conversationId);
          const augmented = model
            ? { ...message, metadata: { ...(message.metadata ?? {}), model } }
            : message;
          // Replace streaming/existing message or add new
          dispatch({ type: 'UPSERT_MESSAGE', payload: augmented });
        });

        connection.on('StreamStarted', (data: { messageId: string; conversationId: string }) => {
          console.log('🟢 Stream started:', data);
          dispatch({ type: 'START_STREAM', payload: data });
        });

        connection.on('StreamChunk', (data: { messageId: string; conversationId: string; text: string }) => {
          dispatch({ type: 'APPEND_STREAM_CHUNK', payload: { messageId: data.messageId, text: data.text } });
        });

        connection.on('MessageProcessing', (data: { conversationId: string; message: string }) => {
          console.log('⏳ Message processing...', data);
          const processingMessage: ChatMessage = {
            id: `processing-${Date.now()}`,
            conversationId: data.conversationId,
            content: 'Processing your request...',
            role: 'Assistant' as any,
            timestamp: new Date().toISOString(),
            status: 'Processing' as any,
            attachments: [],
            tools: [],
          };
          dispatch({ type: 'ADD_MESSAGE', payload: processingMessage });
        });

        connection.on('MessageError', (data: { error: string; conversationId: string }) => {
          console.error('❌ Message error:', data);
          dispatch({ type: 'SET_ERROR', payload: data.error });
        });

        connection.onreconnected(() => {
          console.log('🔄 SignalR reconnected successfully');
          dispatch({ type: 'SET_CONNECTED', payload: true });
          dispatch({ type: 'SET_ERROR', payload: null });
        });

        connection.onclose((error?: Error) => {
          console.log('❌ SignalR connection closed:', error);
          dispatch({ type: 'SET_CONNECTED', payload: false });
        });

        console.log('🔄 Starting SignalR connection...');
        await connection.start();
        
        console.log('✅ SignalR connected successfully! State:', connection.state);
        dispatch({ type: 'SET_CONNECTED', payload: true });
        
      } catch (error) {
        console.error('❌ SignalR connection failed:', error);
        dispatch({ type: 'SET_CONNECTED', payload: false });
        
        // Retry after 3 seconds
        setTimeout(() => {
          console.log('🔄 Retrying SignalR connection...');
          initializeSignalR();
        }, 3000);
      }
    };

    initializeSignalR();

    return () => {
      if (connection) {
        console.log('🧹 Cleaning up SignalR connection...');
        connection.stop();
      }
    };
  }, []);

  const loadConversations = useCallback(async () => {
    try {
      dispatch({ type: 'SET_LOADING', payload: true });
      const conversations = await chatApi.getConversations();
      dispatch({ type: 'SET_CONVERSATIONS', payload: conversations });
    } catch (error) {
      dispatch({ type: 'SET_ERROR', payload: 'Failed to load conversations' });
    } finally {
      dispatch({ type: 'SET_LOADING', payload: false });
    }
  }, []);

  const selectConversation = useCallback(async (conversation: Conversation) => {
    try {
      dispatch({ type: 'SET_CURRENT_CONVERSATION', payload: conversation });
      
      // Join the conversation room
      if (connection) {
        await connection.invoke('JoinConversation', conversation.id);
      }

      // Load messages
      const messages = await chatApi.getMessages(conversation.id);
      dispatch({ type: 'SET_MESSAGES', payload: messages });
    } catch (error) {
      dispatch({ type: 'SET_ERROR', payload: 'Failed to select conversation' });
    }
  }, [connection]);

  const createConversation = useCallback(async (title?: string): Promise<Conversation> => {
    try {
      const conversation = await chatApi.createConversation({ title });
      dispatch({ type: 'ADD_CONVERSATION', payload: conversation });
      return conversation;
    } catch (error) {
      dispatch({ type: 'SET_ERROR', payload: 'Failed to create conversation' });
      throw error;
    }
  }, []);

  const sendMessage = useCallback(async (request: ChatRequest) => {
    try {
      // Track the model selection for this conversation so the response can carry attribution
      const model = request.context?.model as string | undefined;
      if (model) {
        pendingModelRef.current.set(request.conversationId, model);
      }

      // Add user message immediately
      const userMessage: ChatMessage = {
        id: `temp-${Date.now()}`,
        conversationId: request.conversationId,
        content: request.message,
        role: 'User' as any,
        timestamp: new Date().toISOString(),
        status: 'Sending' as any,
        attachments: [],
        tools: [],
      };
      dispatch({ type: 'ADD_MESSAGE', payload: userMessage });

      // Send via SignalR
      if (connection) {
        await connection.invoke('SendMessage', request);
      }
    } catch (error) {
      dispatch({ type: 'SET_ERROR', payload: 'Failed to send message' });
    }
  }, [connection]);

  const deleteConversation = async (conversationId: string) => {
    try {
      await chatApi.deleteConversation(conversationId);
      dispatch({
        type: 'SET_CONVERSATIONS',
        payload: state.conversations.filter(c => c.id !== conversationId),
      });
      
      if (state.currentConversation?.id === conversationId) {
        dispatch({ type: 'SET_CURRENT_CONVERSATION', payload: null });
        dispatch({ type: 'SET_MESSAGES', payload: [] });
      }
    } catch (error) {
      dispatch({ type: 'SET_ERROR', payload: 'Failed to delete conversation' });
    }
  };

  const searchConversations = async (query: string): Promise<Conversation[]> => {
    try {
      return await chatApi.searchConversations(query);
    } catch (error) {
      dispatch({ type: 'SET_ERROR', payload: 'Failed to search conversations' });
      return [];
    }
  };

  const renameConversation = async (conversationId: string, title: string): Promise<void> => {
    try {
      await chatApi.updateConversation(conversationId, title);
      dispatch({ type: 'RENAME_CONVERSATION', payload: { id: conversationId, title } });
    } catch (error) {
      dispatch({ type: 'SET_ERROR', payload: 'Failed to rename conversation' });
    }
  };

  const contextValue: ChatContextType = {
    state,
    loadConversations,
    selectConversation,
    createConversation,
    sendMessage,
    deleteConversation,
    searchConversations,
    renameConversation,
  };

  return (
    <ChatContext.Provider value={contextValue}>
      {children}
    </ChatContext.Provider>
  );
};

export const useChat = (): ChatContextType => {
  const context = useContext(ChatContext);
  if (!context) {
    throw new Error('useChat must be used within a ChatProvider');
  }
  return context;
};