import React, { useState, useEffect } from 'react';
import { ChatProvider, useChat } from './contexts/ChatContext';
import { SettingsProvider } from './contexts/SettingsContext';
import { Header } from './components/Header';
import { ConversationList } from './components/ConversationList';
import { ChatWindow } from './components/ChatWindow';
import { Conversation, ChatMessage, MessageRole, MessageStatus, ChatRequest } from './types/chat';
import { chatApi } from './services/chatApi';
import './styles/App.css';

const AppContent: React.FC = () => {
  const [sidebarOpen, setSidebarOpen] = useState(true);
  const [selectedConversationId, setSelectedConversationId] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [isTyping, setIsTyping] = useState(false);
  
  const { 
    sendMessage, 
    state, 
    loadConversations: loadConversationsFromContext,
    selectConversation,
    createConversation,
    deleteConversation,
    renameConversation
  } = useChat();
  const connectionStatus = state.isConnected ? 'Connected' : 'Disconnected';

  // Load conversations on mount using ChatContext
  useEffect(() => {
    loadConversationsFromContext();
  }, [loadConversationsFromContext]);

  // Load conversation messages when selection changes
  useEffect(() => {
    if (selectedConversationId) {
      const conversation = state.conversations.find(c => c.id === selectedConversationId);
      if (conversation) {
        selectConversation(conversation);
      }
    }
  }, [selectedConversationId, state.conversations, selectConversation]);

  const handleNewConversation = async () => {
    try {
      console.log('Creating new conversation...');
      const newConversation = await createConversation('New Conversation');
      setSelectedConversationId(newConversation.id);
    } catch (error: any) {
      console.error('Failed to create conversation:', error);
    }
  };

  const handleSelectConversation = (id: string) => {
    setSelectedConversationId(id);
  };

  const handleDeleteConversation = async (id: string) => {
    try {
      await deleteConversation(id);
      // If we just deleted the currently selected conversation, clear selection
      if (selectedConversationId === id) {
        setSelectedConversationId(null);
      }
    } catch (error: any) {
      console.error('Failed to delete conversation:', error);
    }
  };

  const handleRenameConversation = async (id: string, title: string) => {
    try {
      await renameConversation(id, title);
    } catch (error: any) {
      console.error('Failed to rename conversation:', error);
    }
  };

  const handleSendMessage = async (content: string, attachments?: File[], model?: string) => {
    if (!selectedConversationId) return;

    try {
      setLoading(true);
      setIsTyping(true);

      // Upload attachments first if any
      const attachmentIds: string[] = [];
      if (attachments && attachments.length > 0) {
        for (const file of attachments) {
          try {
            const formData = new FormData();
            formData.append('file', file);
            formData.append('conversationId', selectedConversationId);
            formData.append('analysisType', 'General'); // Can be inferred from file type later

            const uploadResponse = await fetch('/api/documents/upload', {
              method: 'POST',
              body: formData
            });

            if (uploadResponse.ok) {
              const uploadResult = await uploadResponse.json();
              attachmentIds.push(uploadResult.documentId);
              console.log(`Uploaded ${file.name}, documentId: ${uploadResult.documentId}`);
              
              // If user didn't provide text, auto-generate a message about the uploaded document
              if (!content.trim()) {
                content = `Analyze this ${file.name.split('.').pop()?.toUpperCase()} document for compliance and security issues.`;
              }
            } else {
              console.error(`Failed to upload ${file.name}:`, await uploadResponse.text());
            }
          } catch (uploadError) {
            console.error(`Error uploading ${file.name}:`, uploadError);
          }
        }
      }

      // Send message via SignalR through ChatContext
      const request: ChatRequest = {
        conversationId: selectedConversationId,
        message: content,
        attachmentIds: attachmentIds,
        context: { model: model ?? 'gpt-4o' }
      };

      await sendMessage(request);

      // Auto-name the conversation from the first user message
      const conv = state.conversations.find(c => c.id === selectedConversationId);
      const msgCount = state.messages.filter(m => m.conversationId === selectedConversationId).length;
      if (conv && (conv.title === 'New Conversation' || !conv.title) && msgCount <= 1 && content.trim()) {
        const autoTitle = content.trim().slice(0, 50) + (content.trim().length > 50 ? '…' : '');
        await renameConversation(selectedConversationId, autoTitle);
      }

    } catch (error) {
      console.error('Failed to send message:', error);
    } finally {
      setLoading(false);
      setIsTyping(false);
    }
  };

  const selectedConversation = state.conversations.find(c => c.id === selectedConversationId) || null;

  return (
    <div className="flex flex-col h-screen bg-gradient-to-br from-gray-50 via-gray-100 to-gray-200 dark:from-gray-950 dark:via-gray-900 dark:to-gray-800 text-gray-800 dark:text-gray-100">
      <Header 
        onToggleSidebar={() => setSidebarOpen(!sidebarOpen)}
        sidebarOpen={sidebarOpen}
        currentConversationTitle={selectedConversation?.title}
      />
      <div className="flex flex-1 h-[calc(100vh-60px)]">
        <div className={`w-80 bg-white/80 dark:bg-gray-900/90 backdrop-blur-md border-r border-gray-300 dark:border-gray-700 transition-transform duration-300 overflow-y-auto shadow-lg ${!sidebarOpen ? '-translate-x-full w-0' : ''}`}>
          <ConversationList
            conversations={state.conversations}
            selectedConversationId={selectedConversationId}
            onSelectConversation={handleSelectConversation}
            onNewConversation={handleNewConversation}
            onDeleteConversation={handleDeleteConversation}
            onRenameConversation={handleRenameConversation}
            loading={state.isLoading}
          />
        </div>
        <div className="flex-1 flex flex-col bg-white/50 dark:bg-gray-900/50">
          <ChatWindow
            conversation={selectedConversation}
            messages={state.messages}
            onSendMessage={handleSendMessage}
            loading={loading}
            isTyping={isTyping}
          />
        </div>
      </div>
    </div>
  );
};

function App() {
  console.log('🚀 App component rendering at:', new Date().toISOString());
  
  return (
    <SettingsProvider>
      <ChatProvider>
        <AppContent />
      </ChatProvider>
    </SettingsProvider>
  );
}

export default App;