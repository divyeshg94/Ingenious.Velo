import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { OnboardingService, OnboardingProgressDto } from '../../shared/services/onboarding.service';

interface OnboardingStep {
  label: string;
  description: string;
  done: boolean;
  routerLink: string;
}

/**
 * "First value" nudge: sync one pipeline, view one DORA metric, share a link.
 * Shown on the dashboard until all three are done — orgs that hit all three in their
 * first session are the ones that come back, so this is the fastest lever we have on
 * activation. Dismissible per-session only (not persisted) so it keeps nudging on
 * every visit until the org actually finishes onboarding.
 */
@Component({
  selector: 'velo-onboarding-nudge',
  standalone: true,
  imports: [CommonModule, RouterLink],
  templateUrl: './onboarding-nudge.component.html',
  styleUrls: ['./onboarding-nudge.component.scss'],
})
export class OnboardingNudgeComponent implements OnInit {
  progress: OnboardingProgressDto | null = null;
  dismissed = false;

  constructor(private onboardingService: OnboardingService) {}

  ngOnInit(): void {
    this.onboardingService.getProgress().subscribe({
      next: (p) => (this.progress = p),
      error: () => {}, // non-critical — nudge simply doesn't render
    });
  }

  get steps(): OnboardingStep[] {
    if (!this.progress) return [];
    return [
      {
        label: 'Sync a pipeline',
        description: 'Connect your Azure DevOps org and run your first sync.',
        done: this.progress.hasSyncedPipeline,
        routerLink: '/connections',
      },
      {
        label: 'View a DORA metric',
        description: 'See your Deployment Frequency, Lead Time, and more.',
        done: this.progress.hasViewedDoraMetric,
        routerLink: '/dora',
      },
      {
        label: 'Share a dashboard',
        description: 'Send a read-only link to your DORA results.',
        done: this.progress.hasSharedDashboard,
        routerLink: '/dora',
      },
    ];
  }

  get completedCount(): number {
    return this.steps.filter((s) => s.done).length;
  }

  get shouldShow(): boolean {
    return !this.dismissed && !!this.progress && !this.progress.isComplete;
  }

  dismiss(): void {
    this.dismissed = true;
  }
}
