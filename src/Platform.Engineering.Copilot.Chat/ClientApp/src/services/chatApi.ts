import axios from 'axios';
import { Conversation, ChatMessage, ChatRequest, CreateConversationRequest } from '../types/chat';

const API_BASE_URL = process.env.REACT_APP_API_URL || '';

const apiClient = axios.create({
  baseURL: API_BASE_URL,
  headers: {
    'Content-Type': 'application/json',
  },
});

export const chatApi = {
  // Conversations
  async getConversations(userId: string = 'default-user'): Promise<Conversation[]> {
    const response = await apiClient.get(`/api/conversations?userId=${userId}`);
    return response.data;
  },

  async getConversation(conversationId: string): Promise<Conversation> {
    const response = await apiClient.get(`/api/conversations/${conversationId}`);
    return response.data;
  },

  async createConversation(request: CreateConversationRequest): Promise<Conversation> {
    const response = await apiClient.post('/api/conversations', request);
    return response.data;
  },

  async updateConversation(conversationId: string, title: string): Promise<Conversation> {
    const response = await apiClient.patch(`/api/conversations/${conversationId}/title`, { title });
    return response.data;
  },

  async deleteConversation(conversationId: string): Promise<void> {
    await apiClient.delete(`/api/conversations/${conversationId}`);
  },

  async searchConversations(query: string, userId: string = 'default-user'): Promise<Conversation[]> {
    const response = await apiClient.get(`/api/conversations/search?query=${encodeURIComponent(query)}&userId=${userId}`);
    return response.data;
  },

  // Messages
  async getMessages(conversationId: string): Promise<ChatMessage[]> {
    const response = await apiClient.get(`/api/messages?conversationId=${conversationId}`);
    return response.data;
  },

  async sendMessage(request: ChatRequest): Promise<ChatMessage> {
    const response = await apiClient.post('/api/messages', request);
    return response.data;
  },

  // File upload
  async uploadFile(messageId: string, file: File): Promise<any> {
    const formData = new FormData();
    formData.append('file', file);
    
    const response = await apiClient.post(`/api/messages/${messageId}/attachments`, formData, {
      headers: {
        'Content-Type': 'multipart/form-data',
      },
    });
    
    return response.data;
  },

  // Settings
  async updateGitHubSettings(organization: string, accessToken: string): Promise<void> {
    await apiClient.post('/api/settings/github', { organization, accessToken });
  },

  async testGitHubConnection(org: string, token: string): Promise<{ success: boolean; count?: number; message?: string }> {
    const params = new URLSearchParams();
    if (org) params.set('org', org);
    if (token) params.set('token', token);
    const response = await apiClient.get(`/api/settings/github/test?${params}`);
    const data = response.data;
    return {
      success: data.success ?? true,
      count: data.count,
      message: data.success ? `Connected — ${data.count ?? 0} repos found` : data.error,
    };
  },

  async updateAdoSettings(serverUrl: string, accessToken: string, portalUrl?: string, serverType?: string, collection?: string): Promise<void> {
    await apiClient.post('/api/settings/ado', { serverUrl, accessToken, portalUrl, serverType, collection });
  },

  async testAdoConnection(serverUrl: string, token: string, collection?: string): Promise<{ success: boolean; message?: string }> {
    const params = new URLSearchParams({ serverUrl });
    if (token) params.set('token', token);
    if (collection) params.set('collection', collection);
    const response = await apiClient.get(`/api/settings/ado/test?${params}`);
    return response.data;
  },

  async updateOpenAISettings(apiKey: string, endpoint: string, chatDeployment: string, embeddingDeployment: string): Promise<void> {
    await apiClient.post('/api/settings/openai', { apiKey, endpoint, chatDeployment, embeddingDeployment });
  },

  async updateAzureSettings(
    authMethod: string,
    tenantId: string,
    subscriptionId: string,
    username: string,
    password: string,
    clientId: string,
    clientSecret: string,
    cloudEnvironment: string,
    useManagedIdentity: boolean,
  ): Promise<void> {
    await apiClient.post('/api/settings/azure', {
      authMethod, tenantId, subscriptionId, username, password,
      clientId, clientSecret, cloudEnvironment, useManagedIdentity,
    });
  },

  async testAzureConnection(
    authMethod: string,
    tenantId: string,
    subscriptionId: string,
    username: string,
    password: string,
    clientId: string,
    clientSecret: string,
    cloudEnvironment: string,
  ): Promise<{ success: boolean; message?: string }> {
    const response = await apiClient.post('/api/settings/azure/test', {
      authMethod, tenantId, subscriptionId, username, password,
      clientId, clientSecret, cloudEnvironment,
    });
    return response.data;
  },
};