import { Component, OnInit, HostBinding } from '@angular/core';
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
    :host {
      display: block;
      height: 100vh;
      background-color: var(--background-color, #ffffff);
      color: var(--text-primary-color, #000000);
    }
    .velo-shell { 
      display: flex; 
      flex-direction: column; 
      height: 100%; 
      font-family: "Segoe UI", -apple-system, BlinkMacSystemFont, Roboto, sans-serif; 
    }
    .velo-nav { 
      display: flex; 
      gap: 1rem; 
      padding: 0.75rem 1.5rem; 
      background: var(--header-background-color, #0078d4);
      border-bottom: 1px solid var(--header-border-color, rgba(0,0,0,0.1));
    }
    .velo-nav a { 
      color: var(--header-foreground-color, rgba(255,255,255,0.85)); 
      text-decoration: none; 
      font-size: 14px; 
      padding: 0.25rem 0.5rem; 
      border-radius: 3px;
      transition: all 0.2s ease;
    }
    .velo-nav a.active, .velo-nav a:hover { 
      color: var(--header-foreground-color, #fff); 
      background: var(--primary-color-hover, rgba(255,255,255,0.15)); 
    }
    .velo-content { 
      flex: 1; 
      padding: 1.5rem; 
      overflow: auto;
      background-color: var(--background-color, #ffffff);
    }
  `],
})
export class AppComponent implements OnInit {
  @HostBinding('attr.data-theme') theme: string = 'light';

  ngOnInit(): void {
    // Get initial theme from ADO
    SDK.ready().then(() => {
      const config = SDK.getConfiguration();
      const hostTheme = config['witInputs']?.['theme'] || 'light';
      this.applyTheme(hostTheme);

      // Listen for theme changes
      SDK.register(SDK.getContributionId(), {
        themeChanged: (newTheme: any) => {
          this.applyTheme(newTheme.id || 'light');
        }
      });
    }).catch(() => {
      // Running outside ADO iframe - use light theme
      this.applyTheme('light');
    });
  }

  private applyTheme(themeId: string): void {
    this.theme = themeId;
    document.documentElement.setAttribute('data-theme', themeId);
  }
}

