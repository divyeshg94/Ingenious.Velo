import { Component, Input, OnInit, OnChanges, SimpleChanges } from '@angular/core';
import { CommonModule, DecimalPipe } from '@angular/common';
import { PrMetricsService, PrSizeMetricsDto, PrSizeDistributionDto, ReviewerInsightsDto } from '../../shared/services/pr-metrics.service';

@Component({
  selector: 'velo-pr-insights',
  standalone: true,
  imports: [CommonModule, DecimalPipe],
  templateUrl: './pr-insights.component.html',
  styleUrls: ['./pr-insights.component.scss']
})
export class PrInsightsComponent implements OnInit, OnChanges {
  @Input() projectId: string | null = null;
  @Input() days: number = 30;

  metrics: PrSizeMetricsDto | null = null;
  distribution: PrSizeDistributionDto | null = null;
  reviewers: ReviewerInsightsDto[] = [];

  isLoading = false;
  errorMessage = '';

  constructor(private prMetricsService: PrMetricsService) {}

  ngOnInit(): void {
    if (this.projectId) {
      this.loadMetrics();
    }
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['projectId'] || changes['days']) {
      if (this.projectId) {
        this.loadMetrics();
      }
    }
  }

  loadMetrics(): void {
    if (!this.projectId) return;
    this.isLoading = true;
    this.errorMessage = '';

    // Load all three metrics in parallel
    Promise.all([
      this.prMetricsService.getAveragePrSize(this.projectId, this.days).toPromise(),
      this.prMetricsService.getPrSizeDistribution(this.projectId, this.days).toPromise(),
      this.prMetricsService.getTopReviewers(this.projectId, 10, this.days).toPromise()
    ]).then(([metrics, dist, reviewers]) => {
      this.metrics = metrics || null;
      this.distribution = dist || null;
      this.reviewers = reviewers || [];
      this.isLoading = false;
    }).catch(err => {
      this.errorMessage = 'Failed to load PR metrics';
      this.isLoading = false;
      console.error('PR metrics error:', err);
    });
  }

  /**
   * Determine size category color badge.
   */
  getSizeCategory(totalLines: number): { label: string; color: string } {
    if (totalLines <= 100) return { label: 'Small', color: 'green' };
    if (totalLines <= 500) return { label: 'Medium', color: 'blue' };
    if (totalLines <= 1000) return { label: 'Large', color: 'orange' };
    return { label: 'Extra Large', color: 'red' };
  }

  /**
   * Get color class for approval rate.
   */
  getApprovalRateClass(rate: number): string {
    if (rate >= 90) return 'excellent';
    if (rate >= 70) return 'good';
    if (rate >= 50) return 'fair';
    return 'poor';
  }
}
