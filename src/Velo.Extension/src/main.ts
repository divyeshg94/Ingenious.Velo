import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { AppComponent } from './app/app.component';
import { initializeSDK, getSDK } from './app/shared/services/sdk-initializer.service';

// 1. Initialise SDK  (sets up XDM channel / mock)
// 2. Bootstrap Angular
// 3. Tell the host we are done  (notifyLoadSucceeded)
initializeSDK()
  .then(() => bootstrapApplication(AppComponent, appConfig))
  .then(() => {
    console.log('[App] Velo extension bootstrapped');
    getSDK().notifyLoadSucceeded();
    console.log('[App] notifyLoadSucceeded sent to host');
  })
  .catch((err) => {
    console.error('[App] Startup failed:', err);
    try { getSDK().notifyLoadFailed(String(err)); } catch { /* best-effort */ }
  });
