import React, { createContext, useContext, useState, useEffect, useCallback } from 'react';

export type AzureAuthMethod = 'credentials' | 'servicePrincipal' | 'managedIdentity';

export interface AzureSettings {
  authMethod: AzureAuthMethod;
  tenantId: string;
  subscriptionId: string;
  username: string;
  password: string;
  clientId: string;
  clientSecret: string;
  cloudEnvironment: string;
  useManagedIdentity: boolean;
  enabled: boolean;
}

export type AdoServerType = 'services' | 'server';

export interface AdoSettings {
  serverType: AdoServerType;
  serverUrl: string;
  portalUrl: string;
  token: string;
  enabled: boolean;
}

export interface GitHubSettings {
  organization: string;
  token: string;
  enabled: boolean;
}

export interface OpenAISettings {
  apiKey: string;
  endpoint: string;
  chatDeployment: string;
  embeddingDeployment: string;
}

export interface SecurityBannerSettings {
  enabled: boolean;
  label: string;
  bgColor: string;
  textColor: string;
}

export interface BrandingSettings {
  faviconDataUrl: string;
  homeIconDataUrl: string;
}

export interface AppSettings {
  darkMode: boolean;
  azure: AzureSettings;
  ado: AdoSettings;
  github: GitHubSettings;
  openai: OpenAISettings;
  securityBanner: SecurityBannerSettings;
  branding: BrandingSettings;
}

const DEFAULT_SETTINGS: AppSettings = {
  darkMode: false,
  azure: {
    authMethod: 'servicePrincipal',
    tenantId: '',
    subscriptionId: '',
    username: '',
    password: '',
    clientId: '',
    clientSecret: '',
    cloudEnvironment: 'AzureGovernment',
    useManagedIdentity: false,
    enabled: false,
  },
  ado: {
    serverType: 'services',
    serverUrl: '',
    portalUrl: '',
    token: '',
    enabled: false,
  },
  github: {
    organization: '',
    token: '',
    enabled: false,
  },
  openai: {
    apiKey: '',
    endpoint: '',
    chatDeployment: 'gpt-4o',
    embeddingDeployment: 'text-embedding-ada-002',
  },
  securityBanner: {
    enabled: false,
    label: 'UNCLASSIFIED // FOR OFFICIAL USE ONLY',
    bgColor: '#007a33',
    textColor: '#ffffff',
  },
  branding: {
    faviconDataUrl: '',
    homeIconDataUrl: '',
  },
};

interface SettingsContextValue {
  settings: AppSettings;
  updateSettings: (patch: Partial<AppSettings>) => void;
  updateAzure: (patch: Partial<AzureSettings>) => void;
  updateAdo: (patch: Partial<AdoSettings>) => void;
  updateGitHub: (patch: Partial<GitHubSettings>) => void;
  updateOpenAI: (patch: Partial<OpenAISettings>) => void;
  updateSecurityBanner: (patch: Partial<SecurityBannerSettings>) => void;
  updateBranding: (patch: Partial<BrandingSettings>) => void;
}

const SettingsContext = createContext<SettingsContextValue | undefined>(undefined);

const STORAGE_KEY = 'pec_settings';

export const SettingsProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [settings, setSettings] = useState<AppSettings>(() => {
    try {
      const stored = localStorage.getItem(STORAGE_KEY);
      if (stored) {
        const parsed = JSON.parse(stored);
        // Deep merge so new keys in DEFAULT_SETTINGS are always present
        return {
          ...DEFAULT_SETTINGS,
          ...parsed,
          azure: { ...DEFAULT_SETTINGS.azure, ...parsed.azure },
          ado: { ...DEFAULT_SETTINGS.ado, ...parsed.ado },
          github: { ...DEFAULT_SETTINGS.github, ...parsed.github },
          openai: { ...DEFAULT_SETTINGS.openai, ...parsed.openai },
          securityBanner: { ...DEFAULT_SETTINGS.securityBanner, ...parsed.securityBanner },
          branding: { ...DEFAULT_SETTINGS.branding, ...parsed.branding },
        };
      }
    } catch {
      // ignore
    }
    return DEFAULT_SETTINGS;
  });

  // Apply / remove dark class on <html>
  useEffect(() => {
    if (settings.darkMode) {
      document.documentElement.classList.add('dark');
    } else {
      document.documentElement.classList.remove('dark');
    }
  }, [settings.darkMode]);

  // Apply branding favicon when restored from storage
  useEffect(() => {
    if (settings.branding.faviconDataUrl) {
      const link = document.querySelector("link[rel='icon']") as HTMLLinkElement;
      if (link) link.href = settings.branding.faviconDataUrl;
    }
  }, [settings.branding.faviconDataUrl]);

  // Persist to localStorage whenever settings change
  useEffect(() => {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(settings));
  }, [settings]);

  const updateSettings = useCallback((patch: Partial<AppSettings>) => {
    setSettings(prev => ({ ...prev, ...patch }));
  }, []);

  const updateAzure = useCallback((patch: Partial<AzureSettings>) => {
    setSettings(prev => ({ ...prev, azure: { ...prev.azure, ...patch } }));
  }, []);

  const updateAdo = useCallback((patch: Partial<AdoSettings>) => {
    setSettings(prev => ({ ...prev, ado: { ...prev.ado, ...patch } }));
  }, []);

  const updateGitHub = useCallback((patch: Partial<GitHubSettings>) => {
    setSettings(prev => ({ ...prev, github: { ...prev.github, ...patch } }));
  }, []);

  const updateOpenAI = useCallback((patch: Partial<OpenAISettings>) => {
    setSettings(prev => ({ ...prev, openai: { ...prev.openai, ...patch } }));
  }, []);

  const updateSecurityBanner = useCallback((patch: Partial<SecurityBannerSettings>) => {
    setSettings(prev => ({ ...prev, securityBanner: { ...prev.securityBanner, ...patch } }));
  }, []);

  const updateBranding = useCallback((patch: Partial<BrandingSettings>) => {
    setSettings(prev => ({ ...prev, branding: { ...prev.branding, ...patch } }));
  }, []);

  return (
    <SettingsContext.Provider value={{ settings, updateSettings, updateAzure, updateAdo, updateGitHub, updateOpenAI, updateSecurityBanner, updateBranding }}>
      {children}
    </SettingsContext.Provider>
  );
};

export const useSettings = () => {
  const ctx = useContext(SettingsContext);
  if (!ctx) throw new Error('useSettings must be used within SettingsProvider');
  return ctx;
};
