import React, { useState, useRef } from 'react';
import {
  ArrowLeft, Settings, GitBranch, Palette, Info,
  Check, X, Loader2, Shield, Sun, Moon, Upload,
  Eye, EyeOff, Zap, Bot, Cloud, Database,
} from 'lucide-react';
import { useSettings } from '../contexts/SettingsContext';
import { chatApi } from '../services/chatApi';

interface SettingsPageProps {
  onBack: () => void;
}

type Tab = 'devops' | 'aesthetics' | 'about';
type TestStatus = 'idle' | 'testing' | 'success' | 'error';

const Toggle: React.FC<{ checked: boolean; onChange: () => void }> = ({ checked, onChange }) => (
  <button
    onClick={onChange}
    className={`relative inline-flex h-6 w-11 flex-shrink-0 items-center rounded-full transition-colors focus:outline-none ${checked ? 'bg-blue-600' : 'bg-gray-300 dark:bg-gray-600'}`}
  >
    <span className={`inline-block h-4 w-4 transform rounded-full bg-white shadow transition-transform ${checked ? 'translate-x-6' : 'translate-x-1'}`} />
  </button>
);

const PasswordInput: React.FC<{
  value: string;
  onChange: (v: string) => void;
  placeholder?: string;
  className?: string;
}> = ({ value, onChange, placeholder, className = '' }) => {
  const [show, setShow] = useState(false);
  return (
    <div className="relative">
      <input
        type={show ? 'text' : 'password'}
        value={value}
        onChange={e => onChange(e.target.value)}
        placeholder={placeholder}
        className={`w-full px-3 py-2 pr-9 text-sm border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-800 text-gray-900 dark:text-white placeholder-gray-400 focus:outline-none focus:ring-2 focus:ring-blue-500 ${className}`}
      />
      <button
        type="button"
        onClick={() => setShow(s => !s)}
        className="absolute right-2.5 top-1/2 -translate-y-1/2 text-gray-400 hover:text-gray-600 dark:hover:text-gray-300"
      >
        {show ? <EyeOff size={14} /> : <Eye size={14} />}
      </button>
    </div>
  );
};

const TestButton: React.FC<{
  status: TestStatus;
  onTest: () => void;
  label?: string;
}> = ({ status, onTest, label = 'Test Connection' }) => (
  <button
    onClick={onTest}
    disabled={status === 'testing'}
    className="flex items-center gap-2 px-4 py-2 text-sm font-medium bg-gray-100 dark:bg-gray-800 hover:bg-gray-200 dark:hover:bg-gray-700 rounded-lg transition-colors disabled:opacity-50 text-gray-700 dark:text-gray-300"
  >
    {status === 'testing' && <Loader2 size={14} className="animate-spin text-blue-500" />}
    {status === 'success' && <Check size={14} className="text-green-500" />}
    {status === 'error' && <X size={14} className="text-red-500" />}
    {status === 'idle' && <Zap size={14} className="text-gray-400" />}
    {status === 'testing' ? 'Testing…' : label}
  </button>
);

const agents = [
  {
    icon: '🎯',
    name: 'Orchestrator',
    description:
      'Primary routing agent — analyzes user intent, selects the best-fit specialist agent, and manages conversation flow across the entire platform.',
  },
  {
    icon: '🔍',
    name: 'Discovery Agent',
    description:
      'Azure resource discovery and inventory — list VMs, storage, subscriptions; filter by type, location, or tags; resource health monitoring and dependency mapping.',
  },
  {
    icon: '⚙️',
    name: 'Configuration Agent',
    description:
      'Azure subscription and service principal configuration — set default subscription, manage tenant and SP credentials, answer questions about platform capabilities.',
  },
  {
    icon: '🏗️',
    name: 'Infrastructure Agent',
    description:
      'Custom IaC generation (Bicep/Terraform), Azure resource provisioning with compliance enhancement, Azure Arc onboarding, and scaling analysis.',
  },
  {
    icon: '🌍',
    name: 'Environment Agent',
    description:
      'Template-based environment provisioning — landing zones, AKS clusters, web apps from pre-approved Platform Engineering templates. Drift detection and remediation.',
  },
  {
    icon: '💰',
    name: 'Cost Management Agent',
    description:
      'Real-time Azure cost analysis, budget monitoring, rightsizing recommendations, spending forecasts, and anomaly detection.',
  },
  {
    icon: '🛡️',
    name: 'Compliance Agent',
    description:
      'NIST 800-53, FedRAMP, DoD IL5/IL6, and STIG compliance scanning. Automated remediation planning and execution. SSP, SAR, and POA&M documentation generation.',
  },
  {
    icon: '📚',
    name: 'KnowledgeBase Agent',
    description:
      'Educational content about NIST controls, STIG, RMF, FedRAMP, and DoD Impact Levels. Explains frameworks and requirements — does NOT scan environments.',
  },
  {
    icon: '🔧',
    name: 'DevOps Agent',
    description:
      'GitHub and Azure DevOps automation — repository management, pull requests, issues, CI/CD pipeline triggers, team management, and action runs.',
  },
];

export const SettingsPage: React.FC<SettingsPageProps> = ({ onBack }) => {
  const {
    settings,
    updateSettings,
    updateAzure,
    updateAdo,
    updateGitHub,
    updateOpenAI,
    updateSecurityBanner,
    updateBranding,
  } = useSettings();

  const [activeTab, setActiveTab] = useState<Tab>('devops');
  const [azureTestStatus, setAzureTestStatus] = useState<TestStatus>('idle');
  const [azureTestMsg, setAzureTestMsg] = useState('');
  const [githubTestStatus, setGithubTestStatus] = useState<TestStatus>('idle');
  const [githubTestMsg, setGithubTestMsg] = useState('');
  const [adoTestStatus, setAdoTestStatus] = useState<TestStatus>('idle');
  const [adoTestMsg, setAdoTestMsg] = useState('');
  const [saved, setSaved] = useState<string>('');

  const faviconRef = useRef<HTMLInputElement>(null);
  const iconRef = useRef<HTMLInputElement>(null);

  const flash = (label = 'Saved') => {
    setSaved(label);
    setTimeout(() => setSaved(''), 2500);
  };

  const testAzure = async () => {
    setAzureTestStatus('testing');
    setAzureTestMsg('');
    try {
      const result = await chatApi.testAzureConnection(
        settings.azure.tenantId,
        settings.azure.subscriptionId,
        settings.azure.clientId,
        settings.azure.clientSecret,
        settings.azure.cloudEnvironment,
      );
      setAzureTestStatus(result.success ? 'success' : 'error');
      setAzureTestMsg(result.message ?? (result.success ? 'Connected' : 'Failed'));
    } catch (e: any) {
      setAzureTestStatus('error');
      setAzureTestMsg(e?.response?.data?.error ?? e?.message ?? 'Connection failed');
    }
  };

  const saveAzure = async () => {
    try {
      await chatApi.updateAzureSettings(
        settings.azure.tenantId,
        settings.azure.subscriptionId,
        settings.azure.clientId,
        settings.azure.clientSecret,
        settings.azure.cloudEnvironment,
        settings.azure.useManagedIdentity,
      );
      flash('Azure saved');
    } catch (e) {
      console.error('Failed to save Azure settings', e);
    }
  };

  const testGitHub = async () => {
    setGithubTestStatus('testing');
    setGithubTestMsg('');
    try {
      const result = await chatApi.testGitHubConnection(settings.github.organization, settings.github.token);
      setGithubTestStatus(result.success ? 'success' : 'error');
      setGithubTestMsg(result.message ?? (result.success ? 'Connected' : 'Failed'));
    } catch (e: any) {
      setGithubTestStatus('error');
      setGithubTestMsg(e?.response?.data?.error ?? e?.message ?? 'Connection failed');
    }
  };

  const saveGitHub = async () => {
    try {
      await chatApi.updateGitHubSettings(settings.github.organization, settings.github.token);
      flash('GitHub saved');
    } catch (e) {
      console.error('Failed to save GitHub settings', e);
    }
  };

  const testAdo = async () => {
    setAdoTestStatus('testing');
    setAdoTestMsg('');
    try {
      const result = await chatApi.testAdoConnection(settings.ado.serverUrl, settings.ado.token);
      setAdoTestStatus(result.success ? 'success' : 'error');
      setAdoTestMsg(result.message ?? (result.success ? 'Connected' : 'Failed'));
    } catch (e: any) {
      setAdoTestStatus('error');
      setAdoTestMsg(e?.response?.data?.error ?? e?.message ?? 'Connection failed');
    }
  };

  const saveAdo = async () => {
    try {
      await chatApi.updateAdoSettings(settings.ado.serverUrl, settings.ado.token, settings.ado.portalUrl);
      flash('ADO saved');
    } catch (e) {
      console.error('Failed to save ADO settings', e);
    }
  };

  const saveOpenAI = async () => {
    try {
      await chatApi.updateOpenAISettings(
        settings.openai.apiKey,
        settings.openai.endpoint,
        settings.openai.chatDeployment,
        settings.openai.embeddingDeployment,
      );
      flash('AI settings saved');
    } catch (e) {
      console.error('Failed to save OpenAI settings', e);
    }
  };

  const handleFaviconUpload = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;
    const reader = new FileReader();
    reader.onload = ev => {
      const dataUrl = ev.target?.result as string;
      updateBranding({ faviconDataUrl: dataUrl });
      const link = document.querySelector("link[rel='icon']") as HTMLLinkElement;
      if (link) link.href = dataUrl;
    };
    reader.readAsDataURL(file);
  };

  const handleIconUpload = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;
    const reader = new FileReader();
    reader.onload = ev => {
      updateBranding({ homeIconDataUrl: ev.target?.result as string });
    };
    reader.readAsDataURL(file);
  };

  const inputCls =
    'w-full px-3 py-2 text-sm border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-800 text-gray-900 dark:text-white placeholder-gray-400 focus:outline-none focus:ring-2 focus:ring-blue-500';

  const labelCls = 'block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1.5';

  const sectionCard = 'bg-white dark:bg-gray-900 rounded-xl border border-gray-200 dark:border-gray-700 p-5 space-y-4';

  const tabs: { key: Tab; label: string; icon: React.ReactNode }[] = [
    { key: 'devops', label: 'DevOps & AI', icon: <GitBranch size={16} /> },
    { key: 'aesthetics', label: 'Aesthetics', icon: <Palette size={16} /> },
    { key: 'about', label: 'About', icon: <Info size={16} /> },
  ];

  return (
    <div className="flex flex-col h-screen bg-gray-50 dark:bg-gray-950 text-gray-800 dark:text-gray-100">

      {/* Security banner (shown here too so user sees live preview) */}
      {settings.securityBanner.enabled && (
        <div
          className="px-4 py-1.5 text-center text-sm font-semibold flex-shrink-0 select-none"
          style={{ backgroundColor: settings.securityBanner.bgColor, color: settings.securityBanner.textColor }}
        >
          {settings.securityBanner.label || 'Security Banner'}
        </div>
      )}

      {/* Page header */}
      <div className="flex items-center gap-4 px-6 h-14 bg-white dark:bg-gray-900 border-b border-gray-200 dark:border-gray-700 shadow-sm flex-shrink-0">
        <button
          onClick={onBack}
          className="flex items-center gap-1.5 text-sm text-gray-500 hover:text-gray-800 dark:hover:text-white transition-colors"
        >
          <ArrowLeft size={16} />
          Back to Chat
        </button>
        <div className="w-px h-5 bg-gray-300 dark:bg-gray-600" />
        <div className="flex items-center gap-2">
          <Settings size={18} className="text-blue-600" />
          <h1 className="text-base font-semibold">Settings</h1>
        </div>
        {saved && (
          <div className="ml-auto flex items-center gap-1.5 text-xs text-green-600 dark:text-green-400 font-medium">
            <Check size={12} />
            {saved}
          </div>
        )}
      </div>

      {/* Tab bar */}
      <div className="flex border-b border-gray-200 dark:border-gray-700 bg-white dark:bg-gray-900 px-6 flex-shrink-0">
        {tabs.map(tab => (
          <button
            key={tab.key}
            onClick={() => setActiveTab(tab.key)}
            className={`flex items-center gap-2 px-4 py-3 text-sm font-medium border-b-2 transition-colors -mb-px ${
              activeTab === tab.key
                ? 'border-blue-600 text-blue-600 dark:text-blue-400'
                : 'border-transparent text-gray-500 hover:text-gray-800 dark:hover:text-white hover:border-gray-300'
            }`}
          >
            {tab.icon}
            {tab.label}
          </button>
        ))}
      </div>

      {/* Scrollable content */}
      <div className="flex-1 overflow-y-auto">
        <div className="max-w-3xl mx-auto px-6 py-8 space-y-8">

          {/* ==================== DEVOPS & AI ==================== */}
          {activeTab === 'devops' && (
            <>
              {/* Azure Subscription */}
              <section className="space-y-3">
                <div className="flex items-center gap-2.5">
                  <Database size={20} className="text-blue-500" />
                  <h2 className="text-base font-semibold">Azure Subscription</h2>
                  <span className={`ml-auto text-xs px-2 py-0.5 rounded-full font-medium ${settings.azure.enabled ? 'bg-green-100 text-green-700 dark:bg-green-900/40 dark:text-green-400' : 'bg-gray-100 text-gray-500 dark:bg-gray-800 dark:text-gray-400'}`}>
                    {settings.azure.enabled ? 'Enabled' : 'Disabled'}
                  </span>
                </div>

                <div className={sectionCard}>
                  <div className="flex items-center justify-between">
                    <label className="text-sm font-medium text-gray-700 dark:text-gray-300">Enable Azure integration</label>
                    <Toggle checked={settings.azure.enabled} onChange={() => updateAzure({ enabled: !settings.azure.enabled })} />
                  </div>

                  <div>
                    <label className={labelCls}>Cloud Environment</label>
                    <select
                      value={settings.azure.cloudEnvironment}
                      onChange={e => updateAzure({ cloudEnvironment: e.target.value })}
                      className={inputCls}
                    >
                      <option value="AzureGovernment">Azure Government (MAG)</option>
                      <option value="AzureCloud">Azure Commercial</option>
                      <option value="AzureChina">Azure China</option>
                    </select>
                  </div>

                  <div className="grid grid-cols-2 gap-4">
                    <div>
                      <label className={labelCls}>Tenant ID</label>
                      <input
                        type="text"
                        value={settings.azure.tenantId}
                        onChange={e => updateAzure({ tenantId: e.target.value })}
                        placeholder="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
                        className={inputCls}
                      />
                    </div>
                    <div>
                      <label className={labelCls}>Subscription ID</label>
                      <input
                        type="text"
                        value={settings.azure.subscriptionId}
                        onChange={e => updateAzure({ subscriptionId: e.target.value })}
                        placeholder="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
                        className={inputCls}
                      />
                    </div>
                  </div>

                  <div className="flex items-center justify-between">
                    <div>
                      <label className="text-sm font-medium text-gray-700 dark:text-gray-300">Use Managed Identity</label>
                      <p className="text-xs text-gray-400 mt-0.5">Disable to use a Service Principal instead</p>
                    </div>
                    <Toggle checked={settings.azure.useManagedIdentity} onChange={() => updateAzure({ useManagedIdentity: !settings.azure.useManagedIdentity })} />
                  </div>

                  {!settings.azure.useManagedIdentity && (
                    <>
                      <div>
                        <label className={labelCls}>Client ID <span className="font-normal text-gray-400">(Service Principal App ID)</span></label>
                        <input
                          type="text"
                          value={settings.azure.clientId}
                          onChange={e => updateAzure({ clientId: e.target.value })}
                          placeholder="Service Principal Application ID"
                          className={inputCls}
                        />
                      </div>
                      <div>
                        <label className={labelCls}>Client Secret</label>
                        <PasswordInput
                          value={settings.azure.clientSecret}
                          onChange={v => updateAzure({ clientSecret: v })}
                          placeholder="Service Principal secret"
                        />
                      </div>
                    </>
                  )}

                  <div className="flex flex-wrap items-center gap-3 pt-1">
                    <TestButton status={azureTestStatus} onTest={testAzure} label="Test Azure Connection" />
                    <button
                      onClick={saveAzure}
                      className="px-4 py-2 text-sm font-medium bg-blue-600 hover:bg-blue-700 text-white rounded-lg transition-colors"
                    >
                      Save
                    </button>
                    {azureTestMsg && (
                      <span className={`text-xs ${azureTestStatus === 'success' ? 'text-green-600 dark:text-green-400' : 'text-red-500'}`}>
                        {azureTestMsg}
                      </span>
                    )}
                  </div>

                  <div className="text-xs text-amber-700 dark:text-amber-400 bg-amber-50 dark:bg-amber-900/20 border border-amber-200 dark:border-amber-700 rounded-lg px-3 py-2">
                    ℹ️ Credentials are stored locally in your browser. For server-side access, set{' '}
                    <code className="font-mono">AZURE_TENANT_ID</code>,{' '}
                    <code className="font-mono">AZURE_CLIENT_ID</code>,{' '}
                    <code className="font-mono">AZURE_CLIENT_SECRET</code> in your{' '}
                    <code className="font-mono">.env</code> file.
                  </div>
                </div>
              </section>

              {/* GitHub */}
              <section className="space-y-3">
                <div className="flex items-center gap-2.5">
                  <div className="w-7 h-7 flex items-center justify-center">
                    <svg viewBox="0 0 16 16" className="w-5 h-5 fill-current text-gray-700 dark:text-gray-300">
                      <path d="M8 0C3.58 0 0 3.58 0 8c0 3.54 2.29 6.53 5.47 7.59.4.07.55-.17.55-.38 0-.19-.01-.82-.01-1.49-2.01.37-2.53-.49-2.69-.94-.09-.23-.48-.94-.82-1.13-.28-.15-.68-.52-.01-.53.63-.01 1.08.58 1.23.82.72 1.21 1.87.87 2.33.66.07-.52.28-.87.51-1.07-1.78-.2-3.64-.89-3.64-3.95 0-.87.31-1.59.82-2.15-.08-.2-.36-1.02.08-2.12 0 0 .67-.21 2.2.82.64-.18 1.32-.27 2-.27.68 0 1.36.09 2 .27 1.53-1.04 2.2-.82 2.2-.82.44 1.1.16 1.92.08 2.12.51.56.82 1.27.82 2.15 0 3.07-1.87 3.75-3.65 3.95.29.25.54.73.54 1.48 0 1.07-.01 1.93-.01 2.2 0 .21.15.46.55.38A8.013 8.013 0 0016 8c0-4.42-3.58-8-8-8z" />
                    </svg>
                  </div>
                  <h2 className="text-base font-semibold">GitHub</h2>
                  <span className={`ml-auto text-xs px-2 py-0.5 rounded-full font-medium ${settings.github.enabled ? 'bg-green-100 text-green-700 dark:bg-green-900/40 dark:text-green-400' : 'bg-gray-100 text-gray-500 dark:bg-gray-800 dark:text-gray-400'}`}>
                    {settings.github.enabled ? 'Enabled' : 'Disabled'}
                  </span>
                </div>

                <div className={sectionCard}>
                  <div className="flex items-center justify-between">
                    <label className="text-sm font-medium text-gray-700 dark:text-gray-300">Enable GitHub integration</label>
                    <Toggle checked={settings.github.enabled} onChange={() => updateGitHub({ enabled: !settings.github.enabled })} />
                  </div>

                  <div>
                    <label className={labelCls}>Organization or Username</label>
                    <input
                      type="text"
                      value={settings.github.organization}
                      onChange={e => updateGitHub({ organization: e.target.value })}
                      placeholder="your-org-or-username"
                      className={inputCls}
                    />
                  </div>

                  <div>
                    <label className={labelCls}>
                      Personal Access Token
                      <span className="ml-1 text-gray-400 font-normal">(requires repo, read:org scopes)</span>
                    </label>
                    <PasswordInput
                      value={settings.github.token}
                      onChange={v => updateGitHub({ token: v })}
                      placeholder="ghp_xxxxxxxxxxxxxxxxxxxx"
                    />
                  </div>

                  <div className="flex flex-wrap items-center gap-3 pt-1">
                    <TestButton status={githubTestStatus} onTest={testGitHub} />
                    <button
                      onClick={saveGitHub}
                      className="px-4 py-2 text-sm font-medium bg-blue-600 hover:bg-blue-700 text-white rounded-lg transition-colors"
                    >
                      Save
                    </button>
                    {githubTestMsg && (
                      <span className={`text-xs ${githubTestStatus === 'success' ? 'text-green-600 dark:text-green-400' : 'text-red-500'}`}>
                        {githubTestMsg}
                      </span>
                    )}
                  </div>
                </div>
              </section>

              {/* Azure DevOps */}
              <section className="space-y-3">
                <div className="flex items-center gap-2.5">
                  <Cloud size={20} className="text-blue-600" />
                  <h2 className="text-base font-semibold">Azure DevOps</h2>
                  <span className={`ml-auto text-xs px-2 py-0.5 rounded-full font-medium ${settings.ado.enabled ? 'bg-green-100 text-green-700 dark:bg-green-900/40 dark:text-green-400' : 'bg-gray-100 text-gray-500 dark:bg-gray-800 dark:text-gray-400'}`}>
                    {settings.ado.enabled ? 'Enabled' : 'Disabled'}
                  </span>
                </div>

                <div className={sectionCard}>
                  <div className="flex items-center justify-between">
                    <label className="text-sm font-medium text-gray-700 dark:text-gray-300">Enable Azure DevOps integration</label>
                    <Toggle checked={settings.ado.enabled} onChange={() => updateAdo({ enabled: !settings.ado.enabled })} />
                  </div>

                  <div>
                    <label className={labelCls}>
                      Organization URL
                      <span className="ml-1 text-gray-400 font-normal">(e.g. https://dev.azure.com/yourorg)</span>
                    </label>
                    <input
                      type="url"
                      value={settings.ado.serverUrl}
                      onChange={e => updateAdo({ serverUrl: e.target.value })}
                      placeholder="https://dev.azure.com/yourorg"
                      className={inputCls}
                    />
                  </div>

                  <div>
                    <label className={labelCls}>
                      Portal URL
                      <span className="ml-1 text-gray-400 font-normal">(optional override)</span>
                    </label>
                    <input
                      type="url"
                      value={settings.ado.portalUrl}
                      onChange={e => updateAdo({ portalUrl: e.target.value })}
                      placeholder="https://yourorg.visualstudio.com"
                      className={inputCls}
                    />
                  </div>

                  <div>
                    <label className={labelCls}>Personal Access Token</label>
                    <PasswordInput
                      value={settings.ado.token}
                      onChange={v => updateAdo({ token: v })}
                      placeholder="ADO PAT"
                    />
                  </div>

                  <div className="flex flex-wrap items-center gap-3 pt-1">
                    <TestButton status={adoTestStatus} onTest={testAdo} />
                    <button
                      onClick={saveAdo}
                      className="px-4 py-2 text-sm font-medium bg-blue-600 hover:bg-blue-700 text-white rounded-lg transition-colors"
                    >
                      Save
                    </button>
                    {adoTestMsg && (
                      <span className={`text-xs ${adoTestStatus === 'success' ? 'text-green-600 dark:text-green-400' : 'text-red-500'}`}>
                        {adoTestMsg}
                      </span>
                    )}
                  </div>
                </div>
              </section>

              {/* AI Model */}
              <section className="space-y-3">
                <div className="flex items-center gap-2.5">
                  <Zap size={20} className="text-yellow-500" />
                  <h2 className="text-base font-semibold">AI Model</h2>
                </div>

                <div className={sectionCard}>
                  <div className="rounded-lg bg-amber-50 dark:bg-amber-900/20 border border-amber-200 dark:border-amber-800 px-4 py-3 text-xs text-amber-800 dark:text-amber-300">
                    These settings are stored locally in your browser. To change the backend AI endpoint, update <code className="font-mono">AZURE_OPENAI_API_KEY</code> and <code className="font-mono">AZURE_OPENAI_ENDPOINT</code> in your <code className="font-mono">.env</code> file and restart the service.
                  </div>

                  <div>
                    <label className={labelCls}>Azure OpenAI Endpoint</label>
                    <input
                      type="url"
                      value={settings.openai.endpoint}
                      onChange={e => updateOpenAI({ endpoint: e.target.value })}
                      placeholder="https://your-resource.openai.azure.com/"
                      className={inputCls}
                    />
                  </div>

                  <div>
                    <label className={labelCls}>API Key</label>
                    <PasswordInput
                      value={settings.openai.apiKey}
                      onChange={v => updateOpenAI({ apiKey: v })}
                      placeholder="Your Azure OpenAI API key"
                    />
                  </div>

                  <div className="grid grid-cols-2 gap-4">
                    <div>
                      <label className={labelCls}>Chat Deployment</label>
                      <input
                        type="text"
                        value={settings.openai.chatDeployment}
                        onChange={e => updateOpenAI({ chatDeployment: e.target.value })}
                        placeholder="gpt-4o"
                        className={inputCls}
                      />
                    </div>
                    <div>
                      <label className={labelCls}>Embedding Deployment</label>
                      <input
                        type="text"
                        value={settings.openai.embeddingDeployment}
                        onChange={e => updateOpenAI({ embeddingDeployment: e.target.value })}
                        placeholder="text-embedding-ada-002"
                        className={inputCls}
                      />
                    </div>
                  </div>

                  <div className="flex items-center gap-3 pt-1">
                    <button
                      onClick={saveOpenAI}
                      className="px-4 py-2 text-sm font-medium bg-blue-600 hover:bg-blue-700 text-white rounded-lg transition-colors"
                    >
                      Save AI Settings
                    </button>
                  </div>
                </div>
              </section>
            </>
          )}

          {/* ==================== AESTHETICS ==================== */}
          {activeTab === 'aesthetics' && (
            <>
              {/* Theme */}
              <section className="space-y-3">
                <h2 className="text-base font-semibold">Theme</h2>
                <div className={sectionCard}>
                  <div className="flex items-center justify-between">
                    <div className="flex items-center gap-3">
                      {settings.darkMode
                        ? <Moon size={20} className="text-blue-400" />
                        : <Sun size={20} className="text-yellow-500" />}
                      <div>
                        <p className="text-sm font-medium">Dark Mode</p>
                        <p className="text-xs text-gray-500 dark:text-gray-400">
                          {settings.darkMode ? 'Using dark theme' : 'Using light theme'}
                        </p>
                      </div>
                    </div>
                    <Toggle checked={settings.darkMode} onChange={() => updateSettings({ darkMode: !settings.darkMode })} />
                  </div>
                </div>
              </section>

              {/* Security Banner */}
              <section className="space-y-3">
                <div className="flex items-center gap-2.5">
                  <Shield size={20} className="text-red-500" />
                  <h2 className="text-base font-semibold">Security Banner</h2>
                </div>

                <div className={sectionCard}>
                  {/* Live preview */}
                  <div
                    className="px-4 py-2 text-center text-sm font-semibold rounded-lg transition-all"
                    style={{
                      backgroundColor: settings.securityBanner.enabled ? settings.securityBanner.bgColor : '#e5e7eb',
                      color: settings.securityBanner.enabled ? settings.securityBanner.textColor : '#9ca3af',
                    }}
                  >
                    {settings.securityBanner.label || 'Security Banner Preview'}
                  </div>

                  <div className="flex items-center justify-between">
                    <label className="text-sm font-medium text-gray-700 dark:text-gray-300">Show at top of application</label>
                    <Toggle
                      checked={settings.securityBanner.enabled}
                      onChange={() => updateSecurityBanner({ enabled: !settings.securityBanner.enabled })}
                    />
                  </div>

                  <div>
                    <label className={labelCls}>Banner Message</label>
                    <input
                      type="text"
                      value={settings.securityBanner.label}
                      onChange={e => updateSecurityBanner({ label: e.target.value })}
                      placeholder="UNCLASSIFIED // FOR OFFICIAL USE ONLY"
                      className={inputCls}
                    />
                  </div>

                  <div className="grid grid-cols-2 gap-4">
                    <div>
                      <label className={labelCls}>Background Color</label>
                      <div className="flex items-center gap-2">
                        <input
                          type="color"
                          value={settings.securityBanner.bgColor}
                          onChange={e => updateSecurityBanner({ bgColor: e.target.value })}
                          className="h-9 w-14 rounded-lg border border-gray-300 dark:border-gray-600 cursor-pointer p-0.5 bg-white dark:bg-gray-800"
                        />
                        <input
                          type="text"
                          value={settings.securityBanner.bgColor}
                          onChange={e => updateSecurityBanner({ bgColor: e.target.value })}
                          className="flex-1 px-3 py-2 text-sm border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-800 text-gray-900 dark:text-white font-mono focus:outline-none focus:ring-2 focus:ring-blue-500"
                        />
                      </div>
                    </div>
                    <div>
                      <label className={labelCls}>Text Color</label>
                      <div className="flex items-center gap-2">
                        <input
                          type="color"
                          value={settings.securityBanner.textColor}
                          onChange={e => updateSecurityBanner({ textColor: e.target.value })}
                          className="h-9 w-14 rounded-lg border border-gray-300 dark:border-gray-600 cursor-pointer p-0.5 bg-white dark:bg-gray-800"
                        />
                        <input
                          type="text"
                          value={settings.securityBanner.textColor}
                          onChange={e => updateSecurityBanner({ textColor: e.target.value })}
                          className="flex-1 px-3 py-2 text-sm border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-800 text-gray-900 dark:text-white font-mono focus:outline-none focus:ring-2 focus:ring-blue-500"
                        />
                      </div>
                    </div>
                  </div>

                  <div>
                    <label className={labelCls}>Classification Presets</label>
                    <div className="flex flex-wrap gap-2">
                      {[
                        { label: 'UNCLASSIFIED', bg: '#007a33', text: '#ffffff' },
                        { label: 'UNCLASSIFIED // CUI', bg: '#502b85', text: '#ffffff' },
                        { label: 'SECRET', bg: '#c8102e', text: '#ffffff' },
                        { label: 'TOP SECRET', bg: '#ff8c00', text: '#000000' },
                        { label: 'TOP SECRET // SCI', bg: '#ffff00', text: '#000000' },
                        { label: 'WARNING', bg: '#f59e0b', text: '#000000' },
                      ].map(preset => (
                        <button
                          key={preset.label}
                          onClick={() => updateSecurityBanner({ label: preset.label, bgColor: preset.bg, textColor: preset.text, enabled: true })}
                          className="px-3 py-1 text-xs font-bold rounded border"
                          style={{ backgroundColor: preset.bg, color: preset.text, borderColor: preset.bg }}
                        >
                          {preset.label}
                        </button>
                      ))}
                    </div>
                  </div>
                </div>
              </section>

              {/* Branding */}
              <section className="space-y-3">
                <div className="flex items-center gap-2.5">
                  <Upload size={20} className="text-gray-500 dark:text-gray-400" />
                  <h2 className="text-base font-semibold">Branding</h2>
                </div>

                <div className={sectionCard}>
                  {/* Favicon */}
                  <div className="flex items-center gap-4">
                    <div className="w-12 h-12 rounded-xl bg-gray-100 dark:bg-gray-800 border border-gray-200 dark:border-gray-700 flex items-center justify-center overflow-hidden flex-shrink-0">
                      {settings.branding.faviconDataUrl
                        ? <img src={settings.branding.faviconDataUrl} alt="favicon" className="w-full h-full object-contain" />
                        : <Cloud size={22} className="text-gray-400" />}
                    </div>
                    <div className="flex-1 min-w-0">
                      <p className="text-sm font-medium">Favicon</p>
                      <p className="text-xs text-gray-500 dark:text-gray-400">Shown in browser tab. Recommended: 32×32 PNG or ICO.</p>
                    </div>
                    <input ref={faviconRef} type="file" accept=".png,.ico,.jpg,.svg" className="hidden" onChange={handleFaviconUpload} />
                    <div className="flex items-center gap-2 flex-shrink-0">
                      <button
                        onClick={() => faviconRef.current?.click()}
                        className="flex items-center gap-1.5 px-3 py-1.5 text-sm border border-gray-300 dark:border-gray-600 rounded-lg hover:bg-gray-50 dark:hover:bg-gray-800 transition-colors"
                      >
                        <Upload size={13} />
                        Upload
                      </button>
                      {settings.branding.faviconDataUrl && (
                        <button
                          onClick={() => updateBranding({ faviconDataUrl: '' })}
                          className="p-1.5 text-gray-400 hover:text-red-500 transition-colors"
                          title="Remove"
                        >
                          <X size={14} />
                        </button>
                      )}
                    </div>
                  </div>

                  <div className="border-t border-gray-100 dark:border-gray-800 pt-4" />

                  {/* Home screen icon */}
                  <div className="flex items-center gap-4">
                    <div className="w-12 h-12 rounded-xl bg-gray-100 dark:bg-gray-800 border border-gray-200 dark:border-gray-700 flex items-center justify-center overflow-hidden flex-shrink-0">
                      {settings.branding.homeIconDataUrl
                        ? <img src={settings.branding.homeIconDataUrl} alt="home icon" className="w-full h-full object-contain" />
                        : <Cloud size={22} className="text-gray-400" />}
                    </div>
                    <div className="flex-1 min-w-0">
                      <p className="text-sm font-medium">Home Screen Icon</p>
                      <p className="text-xs text-gray-500 dark:text-gray-400">Used when adding to home screen. Recommended: 192×192 PNG.</p>
                    </div>
                    <input ref={iconRef} type="file" accept=".png,.jpg,.svg" className="hidden" onChange={handleIconUpload} />
                    <div className="flex items-center gap-2 flex-shrink-0">
                      <button
                        onClick={() => iconRef.current?.click()}
                        className="flex items-center gap-1.5 px-3 py-1.5 text-sm border border-gray-300 dark:border-gray-600 rounded-lg hover:bg-gray-50 dark:hover:bg-gray-800 transition-colors"
                      >
                        <Upload size={13} />
                        Upload
                      </button>
                      {settings.branding.homeIconDataUrl && (
                        <button
                          onClick={() => updateBranding({ homeIconDataUrl: '' })}
                          className="p-1.5 text-gray-400 hover:text-red-500 transition-colors"
                          title="Remove"
                        >
                          <X size={14} />
                        </button>
                      )}
                    </div>
                  </div>
                </div>
              </section>
            </>
          )}

          {/* ==================== ABOUT ==================== */}
          {activeTab === 'about' && (
            <>
              {/* Hero */}
              <section>
                <div className="bg-gradient-to-br from-blue-600 to-blue-800 rounded-xl p-6 text-white">
                  <div className="flex items-center gap-4 mb-4">
                    <div className="w-14 h-14 bg-white/20 rounded-xl flex items-center justify-center flex-shrink-0">
                      <Cloud size={28} className="text-white" />
                    </div>
                    <div>
                      <h2 className="text-xl font-bold">Platform Engineering Copilot</h2>
                      <p className="text-blue-200 text-sm">Version 0.8.0 — Enterprise Edition</p>
                    </div>
                  </div>
                  <p className="text-blue-100 text-sm leading-relaxed">
                    An AI-powered platform engineering assistant built for government and enterprise environments.
                    A multi-agent architecture automates DevOps workflows, enforces compliance standards, and
                    accelerates cloud operations — all through natural language.
                  </p>
                </div>
              </section>

              {/* Agents */}
              <section className="space-y-3">
                <div className="flex items-center gap-2.5">
                  <Bot size={20} className="text-blue-600" />
                  <h2 className="text-base font-semibold">AI Agents</h2>
                  <span className="text-xs text-gray-400 ml-1">({agents.length} active)</span>
                </div>

                <div className="grid grid-cols-1 gap-3">
                  {agents.map(agent => (
                    <div
                      key={agent.name}
                      className="bg-white dark:bg-gray-900 rounded-xl border border-gray-200 dark:border-gray-700 p-4 flex items-start gap-4 hover:shadow-sm transition-shadow"
                    >
                      <div className="text-2xl flex-shrink-0 mt-0.5 w-8 text-center">{agent.icon}</div>
                      <div className="min-w-0">
                        <p className="text-sm font-semibold text-gray-900 dark:text-white">{agent.name}</p>
                        <p className="text-xs text-gray-500 dark:text-gray-400 mt-1 leading-relaxed">{agent.description}</p>
                      </div>
                    </div>
                  ))}
                </div>
              </section>

              {/* Keyboard shortcuts */}
              <section className="space-y-3">
                <h2 className="text-base font-semibold">Keyboard Shortcuts</h2>
                <div className="bg-white dark:bg-gray-900 rounded-xl border border-gray-200 dark:border-gray-700 p-5">
                  <div className="grid grid-cols-2 gap-3">
                    {[
                      ['Ctrl+K', 'Toggle sidebar'],
                      ['Ctrl+N', 'New conversation'],
                      ['Ctrl+,', 'Open settings'],
                      ['Enter', 'Send message'],
                      ['Shift+Enter', 'New line'],
                      ['Esc', 'Back to chat'],
                    ].map(([key, desc]) => (
                      <div key={key} className="flex items-center gap-3">
                        <kbd className="bg-gray-100 dark:bg-gray-700 text-gray-700 dark:text-gray-300 px-2 py-0.5 rounded text-xs font-mono flex-shrink-0">
                          {key}
                        </kbd>
                        <span className="text-xs text-gray-600 dark:text-gray-400">{desc}</span>
                      </div>
                    ))}
                  </div>
                </div>
              </section>
            </>
          )}
        </div>
      </div>
    </div>
  );
};
