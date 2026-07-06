import { defaultProductionApiBaseUrl, resolveApiBaseUrl } from './api-base-url';

export const environment = {
  production: true,
  apiUrl: resolveApiBaseUrl(defaultProductionApiBaseUrl)
};
