import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { AppComponent } from './app/app.component';
import { initializeSDK } from './app/shared/services/sdk-initializer.service';

// Initialize SDK (real or mock based on environment)
initializeSDK().then(() => {
  bootstrapApplication(AppComponent, appConfig)
    .then(() => {
      console.log('[App] Velo extension bootstrapped successfully');
    })
    .catch((err) => {
      console.error('[App] Failed to bootstrap Velo extension:', err);
    });
}).catch((err) => {
  console.error('[App] SDK initialization failed:', err);
});
