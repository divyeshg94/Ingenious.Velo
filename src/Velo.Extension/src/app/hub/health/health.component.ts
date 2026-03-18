import { Component } from '@angular/core';

@Component({
  selector: 'velo-health',
  standalone: true,
  template: `
    <div style="padding: 20px; color: var(--text-secondary-color);">
      <h2>❤️ Team Health Metrics</h2>
      <p>Cycle time breakdown, PR quality scores, and test stability trends — coming in Phase 2</p>
    </div>
  `,
})
export class HealthComponent {}
