/**
 * SDK Detection & Initialization Service
 * Detects if running in ADO iframe or local development
 */

import * as SDK from 'azure-devops-extension-sdk';
import { createMockSDK } from './mock-sdk.service';

export const isRunningInADO = (): boolean => {
  // Check if we're running inside an ADO iframe
  return !!(window as any).__VSTS_SDK_HOST__;
};

export const initializeSDK = async () => {
  if (isRunningInADO()) {
    // Running in real ADO iframe
    console.log('[SDK] Running in Azure DevOps');
    try {
      SDK.init({ loaded: false });
      await SDK.ready();
      SDK.notifyLoadSucceeded();
      return SDK;
    } catch (error) {
      console.error('[SDK] Failed to initialize ADO SDK:', error);
      return createMockSDK();
    }
  } else {
    // Running locally - use mock SDK
    console.log('[SDK] Running locally - using mock SDK');
    const mockSDK = createMockSDK({
      orgId: localStorage.getItem('mock-org-id') || 'local-org-dev',
      userId: localStorage.getItem('mock-user-id') || 'local-user-dev',
      token: localStorage.getItem('mock-token') || 'mock-token-for-local-dev',
    });
    mockSDK.init({ loaded: false });
    await mockSDK.ready();
    mockSDK.notifyLoadSucceeded();
    return mockSDK;
  }
};

/**
 * Get the appropriate SDK (real or mock)
 */
export const getSDK = (): any => {
  if (isRunningInADO()) {
    return SDK;
  } else {
    return createMockSDK();
  }
};
