import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { AppComponent } from './app/app.component';
import * as SDK from 'azure-devops-extension-sdk';

SDK.init({ loaded: false });

SDK.ready().then(() => {
  bootstrapApplication(AppComponent, appConfig)
    .then(() => SDK.notifyLoadSucceeded())
    .catch((err) => {
      console.error('Failed to bootstrap Velo extension:', err);
      SDK.notifyLoadFailed(err);
    });
}).catch((err) => {
  console.error('Azure DevOps SDK failed to initialize:', err);
});
