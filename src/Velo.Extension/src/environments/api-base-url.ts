const OverrideStorageKeys = ['velo-api-base-url', 'api-base-url'] as const;

export const defaultDevelopmentApiBaseUrl = 'http://localhost:5001';
export const defaultProductionApiBaseUrl = 'https://api.getvelo.dev';

const normaliseApiBaseUrl = (value: string): string => value.replace(/\/+$/, '');

const parseHttpApiBaseUrl = (value: string): string | null => {
  try {
    const parsed = new URL(value);
    if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') {
      return null;
    }

    return normaliseApiBaseUrl(parsed.toString());
  } catch {
    return null;
  }
};

const readApiBaseUrlOverride = (): string | null => {
  if (typeof window === 'undefined') {
    return null;
  }

  for (const key of OverrideStorageKeys) {
    const value = localStorage.getItem(key)?.trim();
    if (value) {
      const parsed = parseHttpApiBaseUrl(value);
      if (parsed) {
        return parsed;
      }
    }
  }

  return null;
};

export const resolveApiBaseUrl = (defaultApiBaseUrl: string): string => {
  return readApiBaseUrlOverride() ?? normaliseApiBaseUrl(defaultApiBaseUrl);
};
