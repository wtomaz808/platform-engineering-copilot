import React, { useState } from 'react';
import { X, Moon, Sun, GitBranch, Cloud, Info, ChevronRight, ChevronDown, Check, Shield, Loader2 } from 'lucide-react';
import { useSettings } from '../contexts/SettingsContext';
import { chatApi } from '../services/chatApi';

interface AdminPanelProps {
  onClose: () => void;
}

type Tab = 'appearance' | 'integrations' | 'about';

export const AdminPanel: React.FC<AdminPanelProps> = ({ onClose }) => {
  const { settings, updateSettings, updateAdo, updateGitHub, updateAzure } = useSettings();
  const [activeTab, setActiveTab] = useState<Tab>('appearance');
  const [adoExpanded, setAdoExpanded] = useState(settings.ado.enabled);
  const [githubExpanded, setGithubExpanded] = useState(settings.github.enabled);
  const [azureExpanded, setAzureExpanded] = useState(settings.azure.enabled);
  const [saved, setSaved] = useState(false);
  const [azureTestResult, setAzureTestResult] = useState<{ success: boolean; message: string } | null>(null);
  const [azureTesting, setAzureTesting] = useState(false);

  const showSaved = () => {
    setSaved(true);
    setTimeout(() => setSaved(false), 2000);
  };

  const handleSaveAndClose = async () => {
    // Sync GitHub settings to backend if token is provided
    if (settings.github.token && settings.github.enabled) {
      try {
        await chatApi.updateGitHubSettings(settings.github.organization, settings.github.token);
      } catch (e) {
        console.error('Failed to sync GitHub settings to backend:', e);
      }
    }
    // Sync Azure settings to backend if enabled
    if (settings.azure.enabled) {
      try {
        await chatApi.updateAzureSettings(
          settings.azure.authMethod,
          settings.azure.tenantId,
          settings.azure.subscriptionId,
          settings.azure.username,
          settings.azure.password,
          settings.azure.clientId,
          settings.azure.clientSecret,
          settings.azure.cloudEnvironment,
          settings.azure.useManagedIdentity,
        );
      } catch (e) {
        console.error('Failed to sync Azure settings to backend:', e);
      }
    }
    showSaved();
    onClose();
  };

  const handleTestAzure = async () => {
    setAzureTesting(true);
    setAzureTestResult(null);
    try {
      const result = await chatApi.testAzureConnection(
        settings.azure.authMethod,
        settings.azure.tenantId,
        settings.azure.subscriptionId,
        settings.azure.username,
        settings.azure.password,
        settings.azure.clientId,
        settings.azure.clientSecret,
        settings.azure.cloudEnvironment,
      );
      setAzureTestResult({ success: result.success, message: result.message || '' });
    } catch (e: any) {
      setAzureTestResult({ success: false, message: e?.response?.data?.message || e?.message || 'Connection test failed' });
    } finally {
      setAzureTesting(false);
    }
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
                        <label className="block text-xs font-medium text-gray-700 dark:text-gray-300 mb-2">Server Type</label>
                        <div className="grid grid-cols-2 gap-2 mb-4">
                          {([
                            { key: 'services' as const, label: 'Azure DevOps Services', desc: 'Cloud-hosted (dev.azure.us)' },
                            { key: 'server' as const, label: 'Azure DevOps Server', desc: 'On-premises / self-hosted' },
                          ]).map(opt => (
                            <button
                              key={opt.key}
                              onClick={() => updateAdo({ serverType: opt.key })}
                              className={`p-2.5 rounded-lg border text-left transition-colors ${
                                settings.ado.serverType === opt.key
                                  ? 'border-blue-500 bg-blue-50 dark:bg-blue-900/30 ring-1 ring-blue-500'
                                  : 'border-gray-200 dark:border-gray-700 bg-gray-50 dark:bg-gray-800 hover:border-gray-300 dark:hover:border-gray-600'
                              }`}
                            >
                              <p className={`text-xs font-medium ${
                                settings.ado.serverType === opt.key
                                  ? 'text-blue-700 dark:text-blue-400'
                                  : 'text-gray-700 dark:text-gray-300'
                              }`}>{opt.label}</p>
                              <p className="text-[10px] text-gray-500 dark:text-gray-400 mt-0.5">{opt.desc}</p>
                            </button>
                          ))}
                        </div>
                      </div>

                      <div>
                        <label className="block text-xs font-medium text-gray-700 dark:text-gray-300 mb-1">
                          {settings.ado.serverType === 'server' ? 'Server URL' : 'Organization URL'}
                          <span className="ml-1 text-gray-400 font-normal">
                            {settings.ado.serverType === 'server'
                              ? '(e.g. https://your-server:port/tfs)'
                              : '(e.g. https://dev.azure.us/yourorg)'}
                          </span>
                        </label>
                        <input
                          type="url"
                          value={settings.ado.serverUrl}
                          onChange={e => updateAdo({ serverUrl: e.target.value })}
                          placeholder={settings.ado.serverType === 'server' ? 'https://your-server:port/tfs' : 'https://dev.azure.us/yourorg'}
                          className="w-full px-3 py-2 text-sm border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-800 text-gray-900 dark:text-white placeholder-gray-400 focus:outline-none focus:ring-2 focus:ring-blue-500"
                        />
                      </div>

                      {settings.ado.serverType === 'server' && (
                        <div>
                          <label className="block text-xs font-medium text-gray-700 dark:text-gray-300 mb-1">
                            Collection
                            <span className="ml-1 text-gray-400 font-normal">(e.g. DefaultCollection)</span>
                          </label>
                          <input
                            type="text"
                            value={settings.ado.portalUrl}
                            onChange={e => updateAdo({ portalUrl: e.target.value })}
                            placeholder="DefaultCollection"
                            className="w-full px-3 py-2 text-sm border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-800 text-gray-900 dark:text-white placeholder-gray-400 focus:outline-none focus:ring-2 focus:ring-blue-500"
                          />
                        </div>
                      )}
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
                        <p className="text-xs text-gray-400 mt-1">Token is sent to the backend to authenticate GitHub API calls.</p>
                      </div>
                    </div>
                  )}
                </div>

                {/* Azure */}
                <div className="border border-gray-200 dark:border-gray-700 rounded-lg overflow-hidden">
                  <button
                    onClick={() => setAzureExpanded(!azureExpanded)}
                    className="w-full flex items-center justify-between px-4 py-3 bg-gray-50 dark:bg-gray-800 hover:bg-gray-100 dark:hover:bg-gray-750 transition-colors"
                  >
                    <div className="flex items-center gap-3">
                      <div className={`w-2 h-2 rounded-full ${settings.azure.enabled ? 'bg-green-500' : 'bg-gray-300'}`} />
                      <Shield size={16} className="text-blue-500" />
                      <span className="text-sm font-medium text-gray-900 dark:text-white">Azure</span>
                      {settings.azure.enabled && (
                        <span className="text-xs bg-green-100 dark:bg-green-900/40 text-green-700 dark:text-green-400 px-2 py-0.5 rounded-full">Enabled</span>
                      )}
                    </div>
                    {azureExpanded ? <ChevronDown size={16} className="text-gray-400" /> : <ChevronRight size={16} className="text-gray-400" />}
                  </button>

                  {azureExpanded && (
                    <div className="p-4 space-y-4 bg-white dark:bg-gray-900">
                      {/* Enable toggle */}
                      <div className="flex items-center justify-between">
                        <label className="text-sm text-gray-700 dark:text-gray-300">Enable Azure integration</label>
                        <button
                          onClick={() => updateAzure({ enabled: !settings.azure.enabled })}
                          className={`relative inline-flex h-5 w-9 items-center rounded-full transition-colors ${
                            settings.azure.enabled ? 'bg-blue-600' : 'bg-gray-300'
                          }`}
                        >
                          <span className={`inline-block h-3 w-3 transform rounded-full bg-white shadow transition-transform ${
                            settings.azure.enabled ? 'translate-x-5' : 'translate-x-1'
                          }`} />
                        </button>
                      </div>

                      {/* Cloud Environment */}
                      <div>
                        <label className="block text-xs font-medium text-gray-700 dark:text-gray-300 mb-1">Cloud Environment</label>
                        <select
                          value={settings.azure.cloudEnvironment}
                          onChange={e => updateAzure({ cloudEnvironment: e.target.value })}
                          className="w-full px-3 py-2 text-sm border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-800 text-gray-900 dark:text-white focus:outline-none focus:ring-2 focus:ring-blue-500"
                        >
                          <option value="AzureGovernment">Azure Government (US Gov)</option>
                          <option value="AzureCloud">Azure Commercial (Public)</option>
                        </select>
                      </div>

                      {/* Tenant ID */}
                      <div>
                        <label className="block text-xs font-medium text-gray-700 dark:text-gray-300 mb-1">
                          Tenant ID
                          <span className="ml-1 text-gray-400 font-normal">(Entra ID tenant GUID)</span>
                        </label>
                        <input
                          type="text"
                          value={settings.azure.tenantId}
                          onChange={e => updateAzure({ tenantId: e.target.value })}
                          placeholder="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
                          className="w-full px-3 py-2 text-sm border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-800 text-gray-900 dark:text-white placeholder-gray-400 focus:outline-none focus:ring-2 focus:ring-blue-500 font-mono"
                        />
                      </div>

                      {/* Subscription ID */}
                      <div>
                        <label className="block text-xs font-medium text-gray-700 dark:text-gray-300 mb-1">
                          Subscription ID
                          <span className="ml-1 text-gray-400 font-normal">(target Azure subscription)</span>
                        </label>
                        <input
                          type="text"
                          value={settings.azure.subscriptionId}
                          onChange={e => updateAzure({ subscriptionId: e.target.value })}
                          placeholder="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
                          className="w-full px-3 py-2 text-sm border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-800 text-gray-900 dark:text-white placeholder-gray-400 focus:outline-none focus:ring-2 focus:ring-blue-500 font-mono"
                        />
                      </div>

                      {/* Auth Method Selector */}
                      <div>
                        <label className="block text-xs font-medium text-gray-700 dark:text-gray-300 mb-2">Authentication Method</label>
                        <div className="grid grid-cols-3 gap-2">
                          {([
                            { key: 'credentials' as const, label: 'Username / Password', desc: 'Your Azure login' },
                            { key: 'servicePrincipal' as const, label: 'Service Principal', desc: 'App ID + Secret' },
                            { key: 'managedIdentity' as const, label: 'Managed Identity', desc: 'Azure-hosted apps' },
                          ]).map(method => (
                            <button
                              key={method.key}
                              onClick={() => updateAzure({
                                authMethod: method.key,
                                useManagedIdentity: method.key === 'managedIdentity',
                              })}
                              className={`p-2.5 rounded-lg border text-left transition-colors ${
                                settings.azure.authMethod === method.key
                                  ? 'border-blue-500 bg-blue-50 dark:bg-blue-900/30 ring-1 ring-blue-500'
                                  : 'border-gray-200 dark:border-gray-700 bg-gray-50 dark:bg-gray-800 hover:border-gray-300 dark:hover:border-gray-600'
                              }`}
                            >
                              <p className={`text-xs font-medium ${
                                settings.azure.authMethod === method.key
                                  ? 'text-blue-700 dark:text-blue-400'
                                  : 'text-gray-700 dark:text-gray-300'
                              }`}>{method.label}</p>
                              <p className="text-[10px] text-gray-500 dark:text-gray-400 mt-0.5">{method.desc}</p>
                            </button>
                          ))}
                        </div>
                      </div>

                      {/* === Username / Password fields === */}
                      {settings.azure.authMethod === 'credentials' && (
                        <>
                          <div className="p-3 bg-blue-50 dark:bg-blue-900/20 rounded-lg border border-blue-200 dark:border-blue-800">
                            <p className="text-xs text-blue-700 dark:text-blue-400">
                              Enter your Azure / Entra ID login credentials. The account needs <strong>Contributor</strong> role on the target subscription.
                            </p>
                          </div>

                          <div>
                            <label className="block text-xs font-medium text-gray-700 dark:text-gray-300 mb-1">
                              Username
                              <span className="ml-1 text-gray-400 font-normal">(e.g. user@tenant.onmicrosoft.us)</span>
                            </label>
                            <input
                              type="text"
                              value={settings.azure.username}
                              onChange={e => updateAzure({ username: e.target.value })}
                              placeholder="user@yourtenant.onmicrosoft.us"
                              className="w-full px-3 py-2 text-sm border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-800 text-gray-900 dark:text-white placeholder-gray-400 focus:outline-none focus:ring-2 focus:ring-blue-500"
                            />
                          </div>

                          <div>
                            <label className="block text-xs font-medium text-gray-700 dark:text-gray-300 mb-1">
                              Password
                            </label>
                            <input
                              type="password"
                              value={settings.azure.password}
                              onChange={e => updateAzure({ password: e.target.value })}
                              placeholder="••••••••••••"
                              className="w-full px-3 py-2 text-sm border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-800 text-gray-900 dark:text-white placeholder-gray-400 focus:outline-none focus:ring-2 focus:ring-blue-500"
                            />
                          </div>

                          <div>
                            <label className="block text-xs font-medium text-gray-700 dark:text-gray-300 mb-1">
                              Client ID
                              <span className="ml-1 text-gray-400 font-normal">(optional — public app registration, uses Azure PowerShell default if blank)</span>
                            </label>
                            <input
                              type="text"
                              value={settings.azure.clientId}
                              onChange={e => updateAzure({ clientId: e.target.value })}
                              placeholder="Leave blank to use default"
                              className="w-full px-3 py-2 text-sm border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-800 text-gray-900 dark:text-white placeholder-gray-400 focus:outline-none focus:ring-2 focus:ring-blue-500 font-mono"
                            />
                          </div>
                        </>
                      )}

                      {/* === Service Principal fields === */}
                      {settings.azure.authMethod === 'servicePrincipal' && (
                        <>
                          <div className="p-3 bg-blue-50 dark:bg-blue-900/20 rounded-lg border border-blue-200 dark:border-blue-800">
                            <p className="text-xs text-blue-700 dark:text-blue-400">
                              Enter your Service Principal credentials. The SP needs <strong>Contributor</strong> role on the target subscription.
                            </p>
                          </div>

                          <div>
                            <label className="block text-xs font-medium text-gray-700 dark:text-gray-300 mb-1">
                              Client ID
                              <span className="ml-1 text-gray-400 font-normal">(Service Principal App ID)</span>
                            </label>
                            <input
                              type="text"
                              value={settings.azure.clientId}
                              onChange={e => updateAzure({ clientId: e.target.value })}
                              placeholder="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
                              className="w-full px-3 py-2 text-sm border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-800 text-gray-900 dark:text-white placeholder-gray-400 focus:outline-none focus:ring-2 focus:ring-blue-500 font-mono"
                            />
                          </div>

                          <div>
                            <label className="block text-xs font-medium text-gray-700 dark:text-gray-300 mb-1">
                              Client Secret
                              <span className="ml-1 text-gray-400 font-normal">(Service Principal secret value)</span>
                            </label>
                            <input
                              type="password"
                              value={settings.azure.clientSecret}
                              onChange={e => updateAzure({ clientSecret: e.target.value })}
                              placeholder="••••••••••••••••••••"
                              className="w-full px-3 py-2 text-sm border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-800 text-gray-900 dark:text-white placeholder-gray-400 focus:outline-none focus:ring-2 focus:ring-blue-500"
                            />
                          </div>
                        </>
                      )}

                      {/* === Managed Identity info === */}
                      {settings.azure.authMethod === 'managedIdentity' && (
                        <div className="p-3 bg-blue-50 dark:bg-blue-900/20 rounded-lg border border-blue-200 dark:border-blue-800">
                          <p className="text-xs text-blue-700 dark:text-blue-400">
                            Managed Identity is auto-detected when running in Azure (ACI, AKS, App Service).
                            No credentials needed — just ensure the identity has <strong>Contributor</strong> role on the subscription.
                          </p>
                        </div>
                      )}

                      {/* RBAC recommendation */}
                      <div className="p-3 bg-amber-50 dark:bg-amber-900/20 rounded-lg border border-amber-200 dark:border-amber-800">
                        <p className="text-xs text-amber-700 dark:text-amber-400">
                          <strong>RBAC:</strong> The identity needs <strong>Contributor</strong> role on the target subscription for list, monitor, deploy, and delete operations.
                          Add <strong>User Access Administrator</strong> if RBAC management is also needed.
                        </p>
                      </div>

                      {/* Test Connection */}
                      <div className="flex items-center gap-3">
                        <button
                          onClick={handleTestAzure}
                          disabled={azureTesting || !settings.azure.tenantId || !settings.azure.subscriptionId}
                          className="px-4 py-2 text-sm font-medium rounded-lg transition-colors bg-gray-100 dark:bg-gray-700 text-gray-700 dark:text-gray-300 hover:bg-gray-200 dark:hover:bg-gray-600 disabled:opacity-50 disabled:cursor-not-allowed flex items-center gap-2"
                        >
                          {azureTesting ? <Loader2 size={14} className="animate-spin" /> : <Shield size={14} />}
                          {azureTesting ? 'Testing...' : 'Test Connection'}
                        </button>
                        {azureTestResult && (
                          <span className={`text-xs ${azureTestResult.success ? 'text-green-600 dark:text-green-400' : 'text-red-600 dark:text-red-400'}`}>
                            {azureTestResult.message}
                          </span>
                        )}
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
                    'Modernization & Migration Assessment',
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
              onClick={handleSaveAndClose}
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
