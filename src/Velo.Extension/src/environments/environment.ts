import { defaultDevelopmentApiBaseUrl, resolveApiBaseUrl } from './api-base-url';

export const environment = {
  production: false,
  apiUrl: resolveApiBaseUrl(defaultDevelopmentApiBaseUrl)
};
