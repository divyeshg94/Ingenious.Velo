import { Component, OnInit } from '@angular/core';
import { RouterOutlet, RouterLink, RouterLinkActive } from '@angular/router';
import * as SDK from 'azure-devops-extension-sdk';

@Component({
  selector: 'velo-root',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <div class="velo-shell">
      <nav class="velo-nav">
        <a routerLink="/dashboard" routerLinkActive="active">Dashboard</a>
        <a routerLink="/dora" routerLinkActive="active">DORA Metrics</a>
        <a routerLink="/health" routerLinkActive="active">Team Health</a>
        <a routerLink="/agent" routerLinkActive="active">AI Agent</a>
        <a routerLink="/pipelines" routerLinkActive="active">Pipelines</a>
        <a routerLink="/connections" routerLinkActive="active">Connections</a>
      </nav>
      <main class="velo-content">
        <router-outlet />
      </main>
    </div>
  `,
  styles: [`
    .velo-shell { display: flex; flex-direction: column; height: 100vh; font-family: "Segoe UI", sans-serif; }
    .velo-nav { display: flex; gap: 1rem; padding: 0.75rem 1.5rem; background: #0078d4; }
    .velo-nav a { color: rgba(255,255,255,0.85); text-decoration: none; font-size: 14px; padding: 0.25rem 0.5rem; border-radius: 3px; }
    .velo-nav a.active, .velo-nav a:hover { color: #fff; background: rgba(255,255,255,0.15); }
    .velo-content { flex: 1; padding: 1.5rem; overflow: auto; }
  `],
})
export class AppComponent implements OnInit {
  ngOnInit(): void {
    // Initialize the Azure DevOps Extension SDK.
    // This is a no-op when running outside of ADO (e.g. ng serve).
    try {
      SDK.init({ loaded: false });
      SDK.ready().then(() => SDK.notifyLoadSucceeded()).catch(() => {});
    } catch {
      // Running outside ADO iframe — SDK not available
    }
  }
}
