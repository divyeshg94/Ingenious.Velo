import { Component, OnInit, HostBinding } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterOutlet, RouterLink, RouterLinkActive } from '@angular/router';
import { isRunningInADO, getSDK } from './shared/services/sdk-initializer.service';
import { DEV_MOCK_JWT } from './shared/services/mock-sdk.service';

@Component({
  selector: 'velo-root',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <div class="velo-shell">
      <!-- Local Development Header -->
      <div *ngIf="!isADO" class="dev-banner">
        <span class="badge">LOCAL DEVELOPMENT MODE</span>
        <span class="settings">
          <button (click)="openDevSettings()">⚙️ Dev Settings</button>
        </span>
      </div>

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

      <!-- Dev Settings Modal -->
      <div *ngIf="showDevSettings" class="dev-modal">
        <div class="dev-modal-content">
          <h3>Local Development Settings</h3>
          <div class="dev-setting">
            <label>Mock Org ID:</label>
            <input type="text" [(ngModel)]="mockOrgId" placeholder="local-org-dev" />
          </div>
          <div class="dev-setting">
            <label>Mock User ID:</label>
            <input type="text" [(ngModel)]="mockUserId" placeholder="local-user-dev" />
          </div>
          <div class="dev-setting">
            <label>Mock Token:</label>
            <input type="text" [(ngModel)]="mockToken" placeholder="mock-token-for-local-dev" />
          </div>
          <div class="dev-setting">
            <label>API Base URL:</label>
            <input type="text" [(ngModel)]="apiBaseUrl" placeholder="http://localhost:5001" />
          </div>
          <div class="dev-buttons">
            <button (click)="saveDevSettings()" class="btn-save">Save Settings</button>
            <button (click)="showDevSettings = false" class="btn-close">Close</button>
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    :host {
      display: block;
      height: 100vh;
      background-color: var(--background-color);
      color: var(--text-primary-color);
    }
    .velo-shell { 
      display: flex; 
      flex-direction: column; 
      height: 100%; 
      font-family: "Segoe UI", -apple-system, BlinkMacSystemFont, Roboto, sans-serif; 
    }
    .dev-banner {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 8px 16px;
      background-color: #fff4ce;
      border-bottom: 2px solid #ffb900;
      color: #332900;
      font-size: 12px;
      font-weight: 600;
    }
    .badge {
      display: inline-block;
      padding: 4px 12px;
      background-color: #ffb900;
      border-radius: 12px;
      color: #332900;
    }
    .settings button {
      padding: 4px 12px;
      background-color: transparent;
      border: 1px solid #ffb900;
      border-radius: 3px;
      cursor: pointer;
      font-size: 11px;
      font-weight: 600;
    }
    .settings button:hover {
      background-color: #ffb900;
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
      background-color: var(--background-color);
    }
    .dev-modal {
      position: fixed;
      top: 0;
      left: 0;
      right: 0;
      bottom: 0;
      background-color: rgba(0,0,0,0.5);
      display: flex;
      align-items: center;
      justify-content: center;
      z-index: 9999;
    }
    .dev-modal-content {
      background-color: var(--card-background);
      border: 1px solid var(--card-border);
      border-radius: 4px;
      padding: 24px;
      max-width: 400px;
      box-shadow: 0 4px 12px rgba(0,0,0,0.2);
    }
    .dev-modal-content h3 {
      margin-top: 0;
      margin-bottom: 16px;
      color: var(--heading-color);
    }
    .dev-setting {
      margin-bottom: 12px;
    }
    .dev-setting label {
      display: block;
      font-size: 12px;
      font-weight: 600;
      color: var(--text-secondary-color);
      margin-bottom: 4px;
      text-transform: uppercase;
    }
    .dev-setting input {
      width: 100%;
      padding: 8px;
      border: 1px solid var(--border-color);
      border-radius: 2px;
      background-color: var(--background-color);
      color: var(--text-primary-color);
      font-size: 13px;
    }
    .dev-buttons {
      display: flex;
      gap: 8px;
      margin-top: 16px;
      justify-content: flex-end;
    }
    .btn-save, .btn-close {
      padding: 8px 16px;
      border-radius: 2px;
      border: none;
      font-size: 12px;
      font-weight: 600;
      cursor: pointer;
    }
    .btn-save {
      background-color: var(--primary-color);
      color: #ffffff;
    }
    .btn-save:hover {
      background-color: var(--primary-color-hover);
    }
    .btn-close {
      background-color: var(--border-color);
      color: var(--text-primary-color);
    }
  `],
})
export class AppComponent implements OnInit {
  @HostBinding('attr.data-theme') theme: string = 'light';

  isADO = true;
  showDevSettings = false;
  mockOrgId = '';
  mockUserId = '';
  mockToken = '';
  apiBaseUrl = '';

  ngOnInit(): void {
    this.isADO = isRunningInADO();

    // Load dev settings from localStorage
    this.mockOrgId = localStorage.getItem('mock-org-id') || 'local-org-dev';
    this.mockUserId = localStorage.getItem('mock-user-id') || 'local-user-dev';
    this.mockToken = localStorage.getItem('mock-token') || DEV_MOCK_JWT;
    this.apiBaseUrl = localStorage.getItem('api-base-url') || 'http://localhost:5001';

    // Initialize theme detection
    this.detectTheme();
  }

  private detectTheme(): void {
    // Primary detection: read the host page background colour.
    // ADO dark themes use a dark background on the iframe's body.
    const bg = window.getComputedStyle(document.body).backgroundColor;
    const isDark = this.isColorDark(bg);
    this.applyTheme(isDark ? 'dark' : 'light');

    // Secondary: listen for runtime theme changes via ADO SDK.
    try {
      const SDK = getSDK();
      const contributionId = SDK.getContributionId?.();
      if (contributionId) {
        SDK.register?.(contributionId, {
          themeChanged: (newTheme: any) => {
            const id = (newTheme?.id || newTheme?.name || '').toLowerCase();
            this.applyTheme(id.includes('dark') || id.includes('night') ? 'dark' : 'light');
          }
        });
      }
    } catch { /* best-effort */ }

    // Observe background changes (ADO may apply them after initial paint).
    const observer = new MutationObserver(() => {
      const newBg = window.getComputedStyle(document.body).backgroundColor;
      const dark = this.isColorDark(newBg);
      this.applyTheme(dark ? 'dark' : 'light');
    });
    observer.observe(document.body, { attributes: true, attributeFilter: ['style', 'class'] });
  }

  /** Parse an rgb/rgba string and decide if it is a dark colour. */
  private isColorDark(bgColor: string): boolean {
    const m = bgColor.match(/\d+/g);
    if (!m || m.length < 3) return false;
    const [r, g, b] = m.map(Number);
    // Perceived luminance formula
    return (r * 299 + g * 587 + b * 114) / 1000 < 128;
  }

  private applyTheme(themeId: string): void {
    if (this.theme === themeId) return;          // no-op
    this.theme = themeId;
    document.documentElement.setAttribute('data-theme', themeId);
  }

  openDevSettings(): void {
    this.showDevSettings = true;
  }

  saveDevSettings(): void {
    localStorage.setItem('mock-org-id', this.mockOrgId);
    localStorage.setItem('mock-user-id', this.mockUserId);
    localStorage.setItem('mock-token', this.mockToken);
    localStorage.setItem('api-base-url', this.apiBaseUrl);
    
    console.log('[Dev Settings] Saved successfully');
    this.showDevSettings = false;
    
    // Reload to apply new settings
    window.location.reload();
  }
}


