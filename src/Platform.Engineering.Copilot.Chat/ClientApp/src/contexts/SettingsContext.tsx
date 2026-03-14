import React, { createContext, useContext, useState, useEffect, useCallback } from 'react';

export interface AdoSettings {
  serverUrl: string;
  portalUrl: string;
  enabled: boolean;
}

export interface GitHubSettings {
  organization: string;
  token: string;
  enabled: boolean;
}

export interface AppSettings {
  darkMode: boolean;
  ado: AdoSettings;
  github: GitHubSettings;
}

const DEFAULT_SETTINGS: AppSettings = {
  darkMode: false,
  ado: {
    serverUrl: '',
    portalUrl: '',
    enabled: false,
  },
  github: {
    organization: '',
    token: '',
    enabled: false,
  },
};

interface SettingsContextValue {
  settings: AppSettings;
  updateSettings: (patch: Partial<AppSettings>) => void;
  updateAdo: (patch: Partial<AdoSettings>) => void;
  updateGitHub: (patch: Partial<GitHubSettings>) => void;
}

const SettingsContext = createContext<SettingsContextValue | undefined>(undefined);

const STORAGE_KEY = 'pec_settings';

export const SettingsProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [settings, setSettings] = useState<AppSettings>(() => {
    try {
      const stored = localStorage.getItem(STORAGE_KEY);
      if (stored) {
        return { ...DEFAULT_SETTINGS, ...JSON.parse(stored) };
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

  // Persist to localStorage whenever settings change
  useEffect(() => {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(settings));
  }, [settings]);

  const updateSettings = useCallback((patch: Partial<AppSettings>) => {
    setSettings(prev => ({ ...prev, ...patch }));
  }, []);

  const updateAdo = useCallback((patch: Partial<AdoSettings>) => {
    setSettings(prev => ({ ...prev, ado: { ...prev.ado, ...patch } }));
  }, []);

  const updateGitHub = useCallback((patch: Partial<GitHubSettings>) => {
    setSettings(prev => ({ ...prev, github: { ...prev.github, ...patch } }));
  }, []);

  return (
    <SettingsContext.Provider value={{ settings, updateSettings, updateAdo, updateGitHub }}>
      {children}
    </SettingsContext.Provider>
  );
};

export const useSettings = () => {
  const ctx = useContext(SettingsContext);
  if (!ctx) throw new Error('useSettings must be used within SettingsProvider');
  return ctx;
};
