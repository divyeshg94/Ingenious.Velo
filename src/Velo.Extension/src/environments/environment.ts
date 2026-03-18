const apiBaseUrl = localStorage.getItem('api-base-url') || 'http://localhost:5001';

export const environment = {
  production: false,
  apiUrl: apiBaseUrl
};
