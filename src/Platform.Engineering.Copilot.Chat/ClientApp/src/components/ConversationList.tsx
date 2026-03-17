import React, { useState, useEffect, useRef } from 'react';
import { Search, Plus, MessageCircle, Trash2, Pencil, Check, X } from 'lucide-react';
import { Conversation } from '../types/chat';

interface ConversationListProps {
  conversations: Conversation[];
  selectedConversationId: string | null;
  onSelectConversation: (id: string) => void;
  onNewConversation: () => void;
  onDeleteConversation: (id: string) => void;
  onRenameConversation: (id: string, title: string) => void;
  loading: boolean;
}

export const ConversationList: React.FC<ConversationListProps> = ({
  conversations,
  selectedConversationId,
  onSelectConversation,
  onNewConversation,
  onDeleteConversation,
  onRenameConversation,
  loading
}) => {
  const [searchTerm, setSearchTerm] = useState('');
  const [filteredConversations, setFilteredConversations] = useState<Conversation[]>([]);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editingTitle, setEditingTitle] = useState('');
  const editInputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (!searchTerm.trim()) {
      setFilteredConversations(conversations);
    } else {
      const filtered = conversations.filter(conv =>
        conv.title.toLowerCase().includes(searchTerm.toLowerCase()) ||
        conv.description?.toLowerCase().includes(searchTerm.toLowerCase())
      );
      setFilteredConversations(filtered);
    }
  }, [conversations, searchTerm]);

  useEffect(() => {
    if (editingId && editInputRef.current) {
      editInputRef.current.focus();
      editInputRef.current.select();
    }
  }, [editingId]);

  const startEditing = (e: React.MouseEvent, conv: Conversation) => {
    e.stopPropagation();
    setEditingId(conv.id);
    setEditingTitle(conv.title || 'New Conversation');
  };

  const commitRename = (id: string) => {
    const trimmed = editingTitle.trim();
    if (trimmed && trimmed !== 'New Conversation') {
      onRenameConversation(id, trimmed);
    }
    setEditingId(null);
  };

  const cancelRename = () => {
    setEditingId(null);
  };

  const handleRenameKeyDown = (e: React.KeyboardEvent, id: string) => {
    if (e.key === 'Enter') { e.preventDefault(); commitRename(id); }
    if (e.key === 'Escape') { cancelRename(); }
  };

  const formatDate = (dateString: string) => {
    const date = new Date(dateString);
    const now = new Date();
    const diffTime = Math.abs(now.getTime() - date.getTime());
    const diffDays = Math.ceil(diffTime / (1000 * 60 * 60 * 24));

    if (diffDays === 1) {
      return 'Today';
    } else if (diffDays === 2) {
      return 'Yesterday';
    } else if (diffDays <= 7) {
      return `${diffDays - 1} days ago`;
    } else {
      return date.toLocaleDateString();
    }
  };

  if (loading) {
    return (
      <div className="flex flex-col h-full p-4">
        <div className="flex items-center justify-center flex-1 text-gray-500">
          <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-blue-500"></div>
          <span className="ml-3">Loading conversations...</span>
        </div>
      </div>
    );
  }

  return (
    <div className="flex flex-col h-full">
      <div className="flex items-center justify-between p-4 pb-3">
        <h2 className="text-lg font-semibold text-gray-800 dark:text-white">Conversations</h2>
        <button 
          className="p-2 rounded-lg bg-blue-50 dark:bg-blue-900/40 hover:bg-blue-100 dark:hover:bg-blue-900/60 transition-colors duration-200 text-blue-600 dark:text-blue-400"
          onClick={onNewConversation}
          title="Start new conversation"
        >
          <Plus size={16} />
        </button>
      </div>

      <div className="px-4 pb-4">
        <input
          type="text"
          className="w-full px-3 py-2 bg-white dark:bg-gray-800 border border-gray-300 dark:border-gray-600 rounded-lg placeholder-gray-400 dark:placeholder-gray-500 text-gray-800 dark:text-gray-100 focus:outline-none focus:ring-2 focus:ring-blue-400 focus:border-transparent"
          placeholder="Search conversations..."
          value={searchTerm}
          onChange={(e) => setSearchTerm(e.target.value)}
        />
      </div>

      <div className="flex-1 overflow-y-auto px-2">
        {filteredConversations.length === 0 && !loading ? (
          <div className="flex flex-col items-center justify-center py-8 text-center">
            <MessageCircle size={32} className="opacity-50 mb-4 text-gray-400" />
            <p className="text-sm text-gray-500">
              {searchTerm ? 'No conversations found' : 'No conversations yet'}
            </p>
          </div>
        ) : (
          filteredConversations.map((conversation) => (
            <div
              key={conversation.id}
              className={`p-3 mx-2 mb-2 rounded-lg transition-all duration-200 hover:bg-gray-50 dark:hover:bg-gray-800 group ${
                selectedConversationId === conversation.id 
                  ? 'bg-blue-50 dark:bg-blue-900/30 border border-blue-200 dark:border-blue-700 shadow-md' 
                  : 'bg-white dark:bg-gray-850 border border-gray-200 dark:border-gray-700'
              }`}
            >
              <div 
                className="cursor-pointer"
                onClick={() => editingId !== conversation.id && onSelectConversation(conversation.id)}
              >
                <div className="flex justify-between items-start mb-1">
                  {editingId === conversation.id ? (
                    <div className="flex items-center gap-1 flex-1" onClick={e => e.stopPropagation()}>
                      <input
                        ref={editInputRef}
                        type="text"
                        value={editingTitle}
                        onChange={e => setEditingTitle(e.target.value)}
                        onKeyDown={e => handleRenameKeyDown(e, conversation.id)}
                        onBlur={() => commitRename(conversation.id)}
                        className="flex-1 text-sm font-medium bg-white dark:bg-gray-700 border border-blue-400 rounded px-1 py-0.5 text-gray-800 dark:text-gray-100 focus:outline-none focus:ring-1 focus:ring-blue-400"
                        maxLength={80}
                      />
                      <button
                        onMouseDown={e => { e.preventDefault(); commitRename(conversation.id); }}
                        className="p-0.5 text-green-600 hover:text-green-800"
                        title="Save"
                      ><Check size={13} /></button>
                      <button
                        onMouseDown={e => { e.preventDefault(); cancelRename(); }}
                        className="p-0.5 text-gray-400 hover:text-gray-600"
                        title="Cancel"
                      ><X size={13} /></button>
                    </div>
                  ) : (
                    <>
                      <div className="font-medium text-gray-800 dark:text-gray-100 truncate flex-1">
                        {conversation.title || 'New Conversation'}
                      </div>
                      <div className="flex items-center gap-0.5 ml-2 opacity-0 group-hover:opacity-100 transition-opacity duration-200">
                        <button
                          onClick={e => startEditing(e, conversation)}
                          className="p-1 hover:bg-blue-100 dark:hover:bg-blue-900/40 rounded text-blue-500 hover:text-blue-700"
                          title="Rename"
                        ><Pencil size={13} /></button>
                        <button
                          onClick={(e) => {
                            e.stopPropagation();
                            if (window.confirm('Delete this conversation?')) {
                              onDeleteConversation(conversation.id);
                            }
                          }}
                          className="p-1 hover:bg-red-100 rounded text-red-500 hover:text-red-700"
                          title="Delete"
                        ><Trash2 size={13} /></button>
                      </div>
                    </>
                  )}
                </div>
                <div className="flex justify-between text-xs text-gray-500 mb-1">
                  <span>{conversation.messageCount || 0} messages</span>
                  <span>{formatDate(conversation.updatedAt)}</span>
                </div>
                {conversation.description && (
                  <div className="text-xs text-gray-600 mt-1 truncate">
                    {conversation.description}
                  </div>
                )}
              </div>
            </div>
          ))
        )}
      </div>
    </div>
  );
};