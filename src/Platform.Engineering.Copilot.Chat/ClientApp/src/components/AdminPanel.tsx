import React, { useState } from 'react';
import { X, Moon, Sun, GitBranch, Cloud, Info, ChevronRight, ChevronDown, Check } from 'lucide-react';
import { useSettings } from '../contexts/SettingsContext';

interface AdminPanelProps {
  onClose: () => void;
}

type Tab = 'appearance' | 'integrations' | 'about';

export const AdminPanel: React.FC<AdminPanelProps> = ({ onClose }) => {
  const { settings, updateSettings, updateAdo, updateGitHub } = useSettings();
  const [activeTab, setActiveTab] = useState<Tab>('appearance');
  const [adoExpanded, setAdoExpanded] = useState(settings.ado.enabled);
  const [githubExpanded, setGithubExpanded] = useState(settings.github.enabled);
  const [saved, setSaved] = useState(false);

  const showSaved = () => {
    setSaved(true);
    setTimeout(() => setSaved(false), 2000);
  };

  const tabs: { key: Tab; label: string; icon: React.ReactNode }[] = [
    { key: 'appearance', label: 'Appearance', icon: <Sun size={16} /> },
    { key: 'integrations', label: 'Integrations', icon: <GitBranch size={16} /> },
    { key: 'about', label: 'About', icon: <Info size={16} /> },
  ];

  return (
    <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50 p-4">
      <div className="bg-white dark:bg-gray-900 rounded-xl shadow-2xl w-full max-w-2xl max-h-[90vh] flex flex-col overflow-hidden border border-gray-200 dark:border-gray-700">

        {/* Header */}
        <div className="flex items-center justify-between px-6 py-4 border-b border-gray-200 dark:border-gray-700 bg-gray-50 dark:bg-gray-800 flex-shrink-0">
          <div className="flex items-center gap-2">
            <Cloud size={20} className="text-blue-600" />
            <h2 className="text-lg font-semibold text-gray-900 dark:text-white">Admin Settings</h2>
          </div>
          <button
            onClick={onClose}
            className="p-1.5 hover:bg-gray-200 dark:hover:bg-gray-700 rounded-lg transition-colors text-gray-500 dark:text-gray-400"
          >
            <X size={18} />
          </button>
        </div>

        <div className="flex flex-1 min-h-0">
          {/* Sidebar tabs */}
          <nav className="w-44 border-r border-gray-200 dark:border-gray-700 bg-gray-50 dark:bg-gray-800 py-3 flex-shrink-0">
            {tabs.map(tab => (
              <button
                key={tab.key}
                onClick={() => setActiveTab(tab.key)}
                className={`w-full flex items-center gap-2.5 px-4 py-2.5 text-sm transition-colors ${
                  activeTab === tab.key
                    ? 'bg-blue-50 dark:bg-blue-900/40 text-blue-700 dark:text-blue-400 font-medium border-r-2 border-blue-600'
                    : 'text-gray-600 dark:text-gray-400 hover:bg-gray-100 dark:hover:bg-gray-700'
                }`}
              >
                {tab.icon}
                {tab.label}
              </button>
            ))}
          </nav>

          {/* Content */}
          <div className="flex-1 overflow-y-auto p-6">

            {/* === APPEARANCE === */}
            {activeTab === 'appearance' && (
              <div className="space-y-6">
                <h3 className="text-sm font-semibold text-gray-500 dark:text-gray-400 uppercase tracking-wide">Display</h3>

                {/* Dark Mode Toggle */}
                <div className="flex items-center justify-between p-4 bg-gray-50 dark:bg-gray-800 rounded-lg border border-gray-200 dark:border-gray-700">
                  <div className="flex items-center gap-3">
                    {settings.darkMode
                      ? <Moon size={20} className="text-blue-400" />
                      : <Sun size={20} className="text-yellow-500" />
                    }
                    <div>
                      <p className="text-sm font-medium text-gray-900 dark:text-white">Dark Mode</p>
                      <p className="text-xs text-gray-500 dark:text-gray-400">
                        {settings.darkMode ? 'Using dark theme' : 'Using light theme'}
                      </p>
                    </div>
                  </div>
                  <button
                    onClick={() => updateSettings({ darkMode: !settings.darkMode })}
                    className={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors focus:outline-none ${
                      settings.darkMode ? 'bg-blue-600' : 'bg-gray-300'
                    }`}
                  >
                    <span
                      className={`inline-block h-4 w-4 transform rounded-full bg-white shadow transition-transform ${
                        settings.darkMode ? 'translate-x-6' : 'translate-x-1'
                      }`}
                    />
                  </button>
                </div>
              </div>
            )}

            {/* === INTEGRATIONS === */}
            {activeTab === 'integrations' && (
              <div className="space-y-5">
                <h3 className="text-sm font-semibold text-gray-500 dark:text-gray-400 uppercase tracking-wide">Optional Integrations</h3>
                <p className="text-xs text-gray-500 dark:text-gray-400">
                  Configure external integrations to enable additional capabilities. All fields are optional.
                </p>

                {/* Azure DevOps */}
                <div className="border border-gray-200 dark:border-gray-700 rounded-lg overflow-hidden">
                  <button
                    onClick={() => setAdoExpanded(!adoExpanded)}
                    className="w-full flex items-center justify-between px-4 py-3 bg-gray-50 dark:bg-gray-800 hover:bg-gray-100 dark:hover:bg-gray-750 transition-colors"
                  >
                    <div className="flex items-center gap-3">
                      <div className={`w-2 h-2 rounded-full ${settings.ado.enabled ? 'bg-green-500' : 'bg-gray-300'}`} />
                      <span className="text-sm font-medium text-gray-900 dark:text-white">Azure DevOps</span>
                      {settings.ado.enabled && (
                        <span className="text-xs bg-green-100 dark:bg-green-900/40 text-green-700 dark:text-green-400 px-2 py-0.5 rounded-full">Enabled</span>
                      )}
                    </div>
                    {adoExpanded ? <ChevronDown size={16} className="text-gray-400" /> : <ChevronRight size={16} className="text-gray-400" />}
                  </button>

                  {adoExpanded && (
                    <div className="p-4 space-y-4 bg-white dark:bg-gray-900">
                      {/* Enable toggle */}
                      <div className="flex items-center justify-between">
                        <label className="text-sm text-gray-700 dark:text-gray-300">Enable Azure DevOps integration</label>
                        <button
                          onClick={() => updateAdo({ enabled: !settings.ado.enabled })}
                          className={`relative inline-flex h-5 w-9 items-center rounded-full transition-colors ${
                            settings.ado.enabled ? 'bg-blue-600' : 'bg-gray-300'
                          }`}
                        >
                          <span className={`inline-block h-3 w-3 transform rounded-full bg-white shadow transition-transform ${
                            settings.ado.enabled ? 'translate-x-5' : 'translate-x-1'
                          }`} />
                        </button>
                      </div>

                      <div>
                        <label className="block text-xs font-medium text-gray-700 dark:text-gray-300 mb-1">
                          ADO Server URL
                          <span className="ml-1 text-gray-400 font-normal">(e.g. https://dev.azure.com/yourorg)</span>
                        </label>
                        <input
                          type="url"
                          value={settings.ado.serverUrl}
                          onChange={e => updateAdo({ serverUrl: e.target.value })}
                          placeholder="https://dev.azure.com/yourorg"
                          className="w-full px-3 py-2 text-sm border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-800 text-gray-900 dark:text-white placeholder-gray-400 focus:outline-none focus:ring-2 focus:ring-blue-500"
                        />
                      </div>

                      <div>
                        <label className="block text-xs font-medium text-gray-700 dark:text-gray-300 mb-1">
                          ADO Portal URL
                          <span className="ml-1 text-gray-400 font-normal">(optional override)</span>
                        </label>
                        <input
                          type="url"
                          value={settings.ado.portalUrl}
                          onChange={e => updateAdo({ portalUrl: e.target.value })}
                          placeholder="https://yourorg.visualstudio.com"
                          className="w-full px-3 py-2 text-sm border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-800 text-gray-900 dark:text-white placeholder-gray-400 focus:outline-none focus:ring-2 focus:ring-blue-500"
                        />
                      </div>
                    </div>
                  )}
                </div>

                {/* GitHub */}
                <div className="border border-gray-200 dark:border-gray-700 rounded-lg overflow-hidden">
                  <button
                    onClick={() => setGithubExpanded(!githubExpanded)}
                    className="w-full flex items-center justify-between px-4 py-3 bg-gray-50 dark:bg-gray-800 hover:bg-gray-100 dark:hover:bg-gray-750 transition-colors"
                  >
                    <div className="flex items-center gap-3">
                      <div className={`w-2 h-2 rounded-full ${settings.github.enabled ? 'bg-green-500' : 'bg-gray-300'}`} />
                      <span className="text-sm font-medium text-gray-900 dark:text-white">GitHub</span>
                      {settings.github.enabled && (
                        <span className="text-xs bg-green-100 dark:bg-green-900/40 text-green-700 dark:text-green-400 px-2 py-0.5 rounded-full">Enabled</span>
                      )}
                    </div>
                    {githubExpanded ? <ChevronDown size={16} className="text-gray-400" /> : <ChevronRight size={16} className="text-gray-400" />}
                  </button>

                  {githubExpanded && (
                    <div className="p-4 space-y-4 bg-white dark:bg-gray-900">
                      {/* Enable toggle */}
                      <div className="flex items-center justify-between">
                        <label className="text-sm text-gray-700 dark:text-gray-300">Enable GitHub integration</label>
                        <button
                          onClick={() => updateGitHub({ enabled: !settings.github.enabled })}
                          className={`relative inline-flex h-5 w-9 items-center rounded-full transition-colors ${
                            settings.github.enabled ? 'bg-blue-600' : 'bg-gray-300'
                          }`}
                        >
                          <span className={`inline-block h-3 w-3 transform rounded-full bg-white shadow transition-transform ${
                            settings.github.enabled ? 'translate-x-5' : 'translate-x-1'
                          }`} />
                        </button>
                      </div>

                      <div>
                        <label className="block text-xs font-medium text-gray-700 dark:text-gray-300 mb-1">
                          Organization / User
                          <span className="ml-1 text-gray-400 font-normal">(e.g. azurenoops)</span>
                        </label>
                        <input
                          type="text"
                          value={settings.github.organization}
                          onChange={e => updateGitHub({ organization: e.target.value })}
                          placeholder="your-org-or-username"
                          className="w-full px-3 py-2 text-sm border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-800 text-gray-900 dark:text-white placeholder-gray-400 focus:outline-none focus:ring-2 focus:ring-blue-500"
                        />
                      </div>

                      <div>
                        <label className="block text-xs font-medium text-gray-700 dark:text-gray-300 mb-1">
                          Personal Access Token
                          <span className="ml-1 text-gray-400 font-normal">(stored locally only)</span>
                        </label>
                        <input
                          type="password"
                          value={settings.github.token}
                          onChange={e => updateGitHub({ token: e.target.value })}
                          placeholder="ghp_xxxxxxxxxxxx"
                          className="w-full px-3 py-2 text-sm border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-800 text-gray-900 dark:text-white placeholder-gray-400 focus:outline-none focus:ring-2 focus:ring-blue-500"
                        />
                        <p className="text-xs text-gray-400 mt-1">Token is stored in your browser's localStorage and never sent to a server.</p>
                      </div>
                    </div>
                  )}
                </div>
              </div>
            )}

            {/* === ABOUT === */}
            {activeTab === 'about' && (
              <div className="space-y-6">
                <div className="flex items-center gap-4">
                  <div className="w-12 h-12 bg-blue-600 rounded-xl flex items-center justify-center">
                    <Cloud size={24} className="text-white" />
                  </div>
                  <div>
                    <h3 className="text-base font-semibold text-gray-900 dark:text-white">Platform Engineering Copilot</h3>
                    <p className="text-sm text-gray-500 dark:text-gray-400">Version 1.0.0 — PE Copilot</p>
                  </div>
                </div>

                <div className="space-y-3">
                  <h4 className="text-sm font-semibold text-gray-500 dark:text-gray-400 uppercase tracking-wide">Capabilities</h4>
                  {[
                    'ATO Compliance Scanning',
                    'Azure Resource Discovery',
                    'Container Deployment',
                    'Cost Monitoring & Optimization',
                    'Security Assessment',
                    'GitHub DevOps Automation',
                    'Infrastructure as Code Generation',
                  ].map(item => (
                    <div key={item} className="flex items-center gap-2 text-sm text-gray-700 dark:text-gray-300">
                      <Check size={14} className="text-green-500 flex-shrink-0" />
                      {item}
                    </div>
                  ))}
                </div>

                <div className="space-y-2">
                  <h4 className="text-sm font-semibold text-gray-500 dark:text-gray-400 uppercase tracking-wide">Keyboard Shortcuts</h4>
                  <div className="grid grid-cols-2 gap-2 text-sm text-gray-600 dark:text-gray-400">
                    {[
                      ['Ctrl+K', 'Toggle sidebar'],
                      ['Ctrl+N', 'New conversation'],
                      ['Enter', 'Send message'],
                      ['Shift+Enter', 'New line'],
                    ].map(([key, desc]) => (
                      <div key={key} className="flex items-center gap-2">
                        <kbd className="bg-gray-100 dark:bg-gray-700 text-gray-700 dark:text-gray-300 px-1.5 py-0.5 rounded text-xs font-mono">{key}</kbd>
                        <span>{desc}</span>
                      </div>
                    ))}
                  </div>
                </div>
              </div>
            )}
          </div>
        </div>

        {/* Footer */}
        <div className="flex items-center justify-between px-6 py-3 border-t border-gray-200 dark:border-gray-700 bg-gray-50 dark:bg-gray-800 flex-shrink-0">
          <div className={`flex items-center gap-1.5 text-xs transition-all duration-300 ${saved ? 'opacity-100 text-green-600' : 'opacity-0'}`}>
            <Check size={12} />
            Settings saved
          </div>
          <div className="flex gap-3">
            <button
              onClick={() => { showSaved(); onClose(); }}
              className="px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white text-sm font-medium rounded-lg transition-colors"
            >
              Save & Close
            </button>
          </div>
        </div>
      </div>
    </div>
  );
};
