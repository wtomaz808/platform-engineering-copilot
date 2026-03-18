import React from 'react';
import { Menu, MessageSquare, Settings } from 'lucide-react';

interface HeaderProps {
  onToggleSidebar: () => void;
  sidebarOpen: boolean;
  currentConversationTitle?: string;
  onOpenSettings: () => void;
}

export const Header: React.FC<HeaderProps> = ({
  onToggleSidebar,
  sidebarOpen,
  currentConversationTitle,
  onOpenSettings,
}) => {
  return (
    <div className="flex items-center justify-between h-14 px-4 bg-white/80 dark:bg-gray-900/90 backdrop-blur-md border-b border-gray-300 dark:border-gray-700 shadow-sm">
      <div className="flex items-center gap-3">
        <button
          className="p-2 rounded-lg hover:bg-gray-100 dark:hover:bg-gray-800 transition-colors duration-200 text-gray-700 dark:text-gray-300"
          onClick={onToggleSidebar}
          title={sidebarOpen ? 'Close sidebar' : 'Open sidebar'}
        >
          <Menu size={20} />
        </button>
        <MessageSquare size={24} className="text-blue-600" />
        <h1 className="text-lg font-semibold truncate text-gray-800 dark:text-white">
          {currentConversationTitle || 'PE Copilot'}
        </h1>
      </div>
      <div className="flex items-center gap-2">
        <button
          className="p-2 rounded-lg hover:bg-gray-100 dark:hover:bg-gray-800 transition-colors duration-200 text-gray-700 dark:text-gray-300"
          onClick={onOpenSettings}
          title="Settings (Ctrl+,)"
        >
          <Settings size={20} />
        </button>
      </div>
    </div>
  );
};
