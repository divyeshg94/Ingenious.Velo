import { InjectionToken, Provider, EnvironmentProviders } from '@angular/core';

/**
 * Mock Azure DevOps SDK for local development
 * Simulates the ADO SDK when running outside an iframe
 */

export interface MockSDKConfig {
  orgId: string;
  userId: string;
  token: string;
  isLocal: boolean;
}

export const MOCK_SDK_CONFIG = new InjectionToken<MockSDKConfig>('mock-sdk-config');

const DEFAULT_CONFIG: MockSDKConfig = {
  orgId: 'local-org-dev',
  userId: 'local-user-dev',
  token: 'mock-token-for-local-dev',
  isLocal: true,
};

/**
 * Mock SDK implementation - simulates azure-devops-extension-sdk
 */
export const createMockSDK = (config: Partial<MockSDKConfig> = {}) => {
  const finalConfig = { ...DEFAULT_CONFIG, ...config };
  const messageHandlers: { [key: string]: Function } = {};

  return {
    init: (options: any) => {
      console.log('[Mock SDK] Initialized with options:', options);
    },
    ready: async () => {
      console.log('[Mock SDK] SDK ready');
      return Promise.resolve();
    },
    notifyLoadSucceeded: () => {
      console.log('[Mock SDK] Load succeeded');
    },
    notifyLoadFailed: (error: any) => {
      console.error('[Mock SDK] Load failed:', error);
    },
    getAppToken: () => {
      console.log('[Mock SDK] Returning mock token');
      return Promise.resolve(finalConfig.token);
    },
    getConfiguration: () => ({
      witInputs: {
        theme: 'light',
      },
    }),
    getWebContext: () => ({
      project: {
        id: 'mock-project-id',
        name: localStorage.getItem('mock-project-name') || '',
      },
      team: { id: 'mock-team-id', name: 'mock-team' },
      user: { id: finalConfig.userId, name: 'Local Dev User' },
    }),
    getContributionId: () => 'velo.velo-hub',
    register: (id: string, service: any) => {
      console.log('[Mock SDK] Registered service:', id);
      if (typeof service === 'function') {
        messageHandlers[id] = service;
      }
      return true;
    },
  };
};

/**
 * Provider for local development - inject mock SDK
 */
export function provideMockSDK(config?: Partial<MockSDKConfig>): EnvironmentProviders {
  return {
    multi: true,
    useValue: {
      provide: 'SDK',
      useValue: createMockSDK(config),
    },
  } as any;
}
