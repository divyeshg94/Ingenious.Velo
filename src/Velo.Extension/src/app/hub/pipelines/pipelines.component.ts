import { Component } from '@angular/core';

@Component({
  selector: 'velo-pipelines',
  standalone: true,
  template: `
    <div style="padding: 20px; color: var(--text-secondary-color);">
      <h2>🔧 Pipeline Insights</h2>
      <p>Pipeline run history, performance analysis, and failure trends — coming in Phase 2</p>
    </div>
  `,
})
export class PipelinesComponent {}
