import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'velo-dashboard',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="velo-page">
      <h1>Velo Dashboard</h1>
      <p class="velo-subtitle">DORA metrics and engineering intelligence for your project.</p>

      <div class="velo-cards">
        <div class="velo-card placeholder">
          <h3>Deployment Frequency</h3>
          <p class="velo-metric">—</p>
          <p class="velo-label">deployments / day</p>
        </div>
        <div class="velo-card placeholder">
          <h3>Lead Time for Changes</h3>
          <p class="velo-metric">—</p>
          <p class="velo-label">hours (avg)</p>
        </div>
        <div class="velo-card placeholder">
          <h3>Change Failure Rate</h3>
          <p class="velo-metric">—</p>
          <p class="velo-label">percent</p>
        </div>
        <div class="velo-card placeholder">
          <h3>Mean Time to Restore</h3>
          <p class="velo-metric">—</p>
          <p class="velo-label">hours (avg)</p>
        </div>
        <div class="velo-card placeholder">
          <h3>Rework Rate</h3>
          <p class="velo-metric">—</p>
          <p class="velo-label">percent</p>
        </div>
      </div>

      <p class="velo-notice">Connect your Azure DevOps organization via the Connections tab to start ingesting data.</p>
    </div>
  `,
  styles: [`
    .velo-page { max-width: 1100px; }
    h1 { margin: 0 0 0.25rem; font-size: 22px; color: #1b1b1b; }
    .velo-subtitle { color: #555; margin: 0 0 1.5rem; }
    .velo-cards { display: grid; grid-template-columns: repeat(auto-fill, minmax(200px, 1fr)); gap: 1rem; margin-bottom: 1.5rem; }
    .velo-card { padding: 1.25rem; border: 1px solid #e0e0e0; border-radius: 6px; background: #fff; }
    .velo-card h3 { margin: 0 0 0.75rem; font-size: 13px; color: #555; font-weight: 600; text-transform: uppercase; letter-spacing: 0.03em; }
    .velo-metric { font-size: 32px; font-weight: 700; color: #0078d4; margin: 0 0 0.25rem; }
    .velo-label { font-size: 12px; color: #888; margin: 0; }
    .velo-notice { font-size: 13px; color: #888; border-left: 3px solid #0078d4; padding-left: 0.75rem; }
  `],
})
export class DashboardComponent {}
