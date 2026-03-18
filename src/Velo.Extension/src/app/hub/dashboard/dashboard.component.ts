import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { DoraMetricsService, DoraMetricsDto } from '../../shared/services/dora-metrics.service';

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
  selectedProjectId: string | null = null;

  constructor(private doraService: DoraMetricsService) {}

  ngOnInit(): void {
    // Get selected project from sessionStorage (set by Connections component)
    this.selectedProjectId = sessionStorage.getItem('selectedProjectId');
    
    if (this.selectedProjectId) {
      console.log('[Dashboard] ngOnInit: Project selected -', this.selectedProjectId);
      this.loadMetrics();
    } else {
      console.log('[Dashboard] ngOnInit: No project selected yet');
    }
  }

  loadMetrics(): void {
    if (!this.selectedProjectId) {
      this.errorMessage = '📁 Please select a project in the Connections tab first.';
      return;
    }

    this.isLoading = true;
    this.errorMessage = '';

    console.log('[Dashboard] Calling API: GET /api/dora/latest for project:', this.selectedProjectId);

    this.doraService.getLatestMetrics(this.selectedProjectId).subscribe({
      next: (metrics) => {
        console.log('[Dashboard] ✅ API Response received:', metrics);
        this.metrics = metrics;
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
