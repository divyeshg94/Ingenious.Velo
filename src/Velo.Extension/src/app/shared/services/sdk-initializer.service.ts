/**
 * SDK Detection & Initialization Service
 * Detects if running in ADO iframe or local development.
 *
 * Design rules
 * 1. Detect ADO by checking we are in an iframe (window.parent !== window).
 * 2. Keep ONE singleton SDK/mock-SDK reference so every consumer shares state.
 * 3. Never call SDK.ready() more than once.
 */

import * as SDK from 'azure-devops-extension-sdk';
import { createMockSDK } from './mock-sdk.service';

/** Cached reference – set once by initializeSDK(). */
let sdkInstance: any = null;

/** True when the extension is loaded inside the ADO host iframe. */
export const isRunningInADO = (): boolean => {
  try {
    return window.self !== window.top;
  } catch {
    // cross-origin restriction → definitely in an iframe
    return true;
  }
};

/**
 * Initialise the SDK exactly once.
 * Returns a reference that is also cached for getSDK().
 */
export const initializeSDK = async (): Promise<any> => {
  if (sdkInstance) {
    return sdkInstance;          // already initialised
  }

  if (isRunningInADO()) {
    console.log('[SDK] Running in Azure DevOps iframe');
    // SDK.init sets up the XDM channel with the host.
    // loaded:false tells the host "I will call notifyLoadSucceeded later".
    SDK.init({ loaded: false });

    // SDK.ready() resolves once the handshake completes.
    await SDK.ready();
    console.log('[SDK] ADO SDK ready');

    sdkInstance = SDK;
    return sdkInstance;
  }

  // ── Local development ────────────────────────────────────
  console.log('[SDK] Running locally – using mock SDK');
  const mockSDK = createMockSDK({
    orgId: localStorage.getItem('mock-org-id') || 'local-org-dev',
    userId: localStorage.getItem('mock-user-id') || 'local-user-dev',
    token: localStorage.getItem('mock-token') || 'mock-token-for-local-dev',
  });
  mockSDK.init({ loaded: false });
  await mockSDK.ready();
  console.log('[SDK] Mock SDK ready');

  sdkInstance = mockSDK;
  return sdkInstance;
};

/**
 * Return the initialised SDK singleton.
 * Falls back to the real SDK import if called before init (should not happen).
 */
export const getSDK = (): any => {
  return sdkInstance ?? SDK;
};
