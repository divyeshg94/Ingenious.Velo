import { Component } from '@angular/core';

@Component({
  selector: 'velo-agent',
  standalone: true,
  template: `
    <div style="padding: 20px; color: var(--text-secondary-color);">
      <h2>🤖 Foundry AI Agent</h2>
      <p>AI-powered pipeline analysis and optimization recommendations — coming in Phase 2</p>
    </div>
  `,
})
export class AgentComponent {}
