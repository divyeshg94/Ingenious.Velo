const OverrideStorageKeys = ['velo-api-base-url', 'api-base-url'] as const;

export const defaultDevelopmentApiBaseUrl = 'http://localhost:5001';
export const defaultProductionApiBaseUrl = 'https://api.getvelo.dev';

const normaliseApiBaseUrl = (value: string): string => value.replace(/\/+$/, '');

const readApiBaseUrlOverride = (): string | null => {
  if (typeof window === 'undefined') {
    return null;
  }

  for (const key of OverrideStorageKeys) {
    const value = localStorage.getItem(key)?.trim();
    if (value) {
      return normaliseApiBaseUrl(value);
    }
  }

  return null;
};

export const resolveApiBaseUrl = (defaultApiBaseUrl: string): string => {
  return readApiBaseUrlOverride() ?? normaliseApiBaseUrl(defaultApiBaseUrl);
};
