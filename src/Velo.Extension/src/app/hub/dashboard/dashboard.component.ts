import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { DoraMetricsService, DoraMetricsDto } from '../../shared/services/dora-metrics.service';
import { getSDK, isRunningInADO } from '../../shared/services/sdk-initializer.service';

@Component({
  selector: 'velo-dashboard',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './dashboard.component.html',
  styleUrls: ['./dashboard.component.scss']
})
export class DashboardComponent implements OnInit {
  metrics: DoraMetricsDto | null = null;
  isLoading = false;
  errorMessage = '';
  gatheringMessage = '';
  selectedProjectId: string | null = null;

  constructor(private doraService: DoraMetricsService) {}

  ngOnInit(): void {
    // 1. Try sessionStorage first (set by Connections component)
    this.selectedProjectId = sessionStorage.getItem('selectedProjectId');

    // 2. In ADO, auto-detect the project from the SDK host context
    if (!this.selectedProjectId && isRunningInADO()) {
      try {
        const SDK = getSDK();
        const webContext = SDK.getWebContext?.();
        if (webContext?.project?.name) {
          this.selectedProjectId = webContext.project.name;
          sessionStorage.setItem('selectedProjectId', this.selectedProjectId!);
          console.log('[Dashboard] Auto-detected ADO project:', this.selectedProjectId);
        }
      } catch {
        console.log('[Dashboard] Could not auto-detect project from ADO context');
      }
    }

    if (this.selectedProjectId) {
      console.log('[Dashboard] Loading metrics for project:', this.selectedProjectId);
      this.loadMetrics();
    } else {
      console.log('[Dashboard] No project selected yet');
    }
  }

  loadMetrics(): void {
    if (!this.selectedProjectId) {
      this.errorMessage = '📁 Please select a project in the Connections tab first.';
      return;
    }

    this.isLoading = true;
    this.errorMessage = '';
    this.gatheringMessage = '';

    console.log('[Dashboard] Calling API: GET /api/dora/latest for project:', this.selectedProjectId);

    this.doraService.getLatestMetrics(this.selectedProjectId).subscribe({
      next: (response: any) => {
        console.log('[Dashboard] ✅ API Response received:', response);

        // The API returns { status: "gathering", message: "..." } when no metrics exist yet
        if (response?.status === 'gathering') {
          this.gatheringMessage = response.message;
          this.metrics = null;
        } else {
          this.metrics = response as DoraMetricsDto;
          this.gatheringMessage = '';
        }
        this.isLoading = false;
      },
      error: (err) => {
        console.error('[Dashboard] ❌ API Error:', err);
        this.errorMessage = 'Failed to load metrics: ' + (err.message || 'Please check your connection and try again.');
        this.isLoading = false;
        this.metrics = null;
      }
    });
  }

  getRatingClass(rating: string): string {
    return 'rating-' + (rating || 'low').toLowerCase();
  }
}
