import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { DoraMetricsService, DoraMetricsDto } from '../../shared/services/dora-metrics.service';
import { TeamMappingService, TeamMappingDto } from '../../shared/services/team-mapping.service';
import { getSDK, isRunningInADO } from '../../shared/services/sdk-initializer.service';

interface MetricScore {
  label: string;
  rating: string;
  score: number;
  cls: string;
}

@Component({
  selector: 'velo-dashboard',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './dashboard.component.html',
  styleUrls: ['./dashboard.component.scss']
})
export class DashboardComponent implements OnInit {
  metrics: DoraMetricsDto | null = null;
  isLoading = false;
  errorMessage = '';
  gatheringMessage = '';
  selectedProjectId: string | null = null;
  repositories: string[] = [];
  teamMappings: TeamMappingDto[] = [];
  selectedRepository: string | null = null;

  constructor(private doraService: DoraMetricsService, private teamMappingService: TeamMappingService) {}

  ngOnInit(): void {
    this.selectedProjectId = sessionStorage.getItem('selectedProjectId');

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
      this.loadRepositories();
      this.loadMetrics();
    }
  }

  loadRepositories(): void {
    if (!this.selectedProjectId) return;
    this.teamMappingService.getMappings(this.selectedProjectId).subscribe({
      next: mappings => this.teamMappings = mappings,
      error: () => {}
    });
    this.teamMappingService.getRepositories(this.selectedProjectId).subscribe({
      next: repos => this.repositories = repos,
      error: () => {}
    });
  }

  onRepositoryChange(repo: string | null): void {
    this.selectedRepository = repo;
    this.loadMetrics();
  }

  getTeamLabel(repo: string): string {
    const mapping = this.teamMappings.find(m => m.repositoryName === repo);
    return mapping ? `${mapping.teamName} (${repo})` : repo;
  }

  loadMetrics(): void {
    if (!this.selectedProjectId) {
      this.errorMessage = 'Please select a project in the Connections tab first.';
      return;
    }

    this.isLoading = true;
    this.errorMessage = '';
    this.gatheringMessage = '';

    this.doraService.getLatestMetrics(this.selectedProjectId, this.selectedRepository ?? undefined).subscribe({
      next: (response: any) => {
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
        console.error('[Dashboard] API Error:', err);
        this.errorMessage = err.message || 'Failed to load metrics. Please check your connection and try again.';
        this.isLoading = false;
        this.metrics = null;
      }
    });
  }

  getRatingKey(rating: string): string {
    const r = (rating || '').toLowerCase();
    if (r === 'elite') return 'elite';
    if (r === 'high') return 'high';
    if (r === 'medium') return 'medium';
    return 'low';
  }

  getRatingBadgeClass(rating: string): string {
    return 'badge-' + this.getRatingKey(rating);
  }

  getOverallHealthLabel(): string {
    const scores = this.getMetricScores();
    const elite = scores.filter(s => s.rating.toLowerCase() === 'elite').length;
    const high = scores.filter(s => s.rating.toLowerCase() === 'high').length;
    if (elite >= 4) return 'Elite Performer';
    if (elite + high >= 4) return 'High Performer';
    if (elite + high >= 2) return 'Good Performance';
    return 'Needs Improvement';
  }

  getOverallHealthClass(): string {
    const label = this.getOverallHealthLabel();
    if (label === 'Elite Performer') return 'health--excellent';
    if (label === 'High Performer') return 'health--good';
    if (label === 'Good Performance') return 'health--fair';
    return 'health--poor';
  }

  formatLeadTime(hours: number): string {
    if (hours < 1) return Math.round(hours * 60).toString();
    if (hours < 24) return hours.toFixed(1);
    return (hours / 24).toFixed(1);
  }

  leadTimeUnit(hours: number): string {
    if (hours < 1) return 'min';
    if (hours < 24) return 'hrs';
    return 'days';
  }

  formatMttr(hours: number): string {
    if (hours < 1) return Math.round(hours * 60).toString();
    return hours.toFixed(1);
  }

  mttrUnit(hours: number): string {
    return hours < 1 ? 'min' : 'hrs';
  }

  getMetricScores(): MetricScore[] {
    if (!this.metrics) return [];
    return [
      { label: 'Deploy Freq', rating: this.metrics.deploymentFrequencyRating, score: this.ratingToScore(this.metrics.deploymentFrequencyRating), cls: this.getRatingBadgeClass(this.metrics.deploymentFrequencyRating) },
      { label: 'Lead Time', rating: this.metrics.leadTimeRating, score: this.ratingToScore(this.metrics.leadTimeRating), cls: this.getRatingBadgeClass(this.metrics.leadTimeRating) },
      { label: 'Failure Rate', rating: this.metrics.changeFailureRating, score: this.ratingToScore(this.metrics.changeFailureRating), cls: this.getRatingBadgeClass(this.metrics.changeFailureRating) },
      { label: 'MTTR', rating: this.metrics.mttrRating, score: this.ratingToScore(this.metrics.mttrRating), cls: this.getRatingBadgeClass(this.metrics.mttrRating) },
      { label: 'Rework Rate', rating: this.metrics.reworkRateRating, score: this.ratingToScore(this.metrics.reworkRateRating), cls: this.getRatingBadgeClass(this.metrics.reworkRateRating) },
    ];
  }

  getInsightType(rating: string): string {
    const r = (rating || '').toLowerCase();
    if (r === 'elite') return 'ok';
    if (r === 'high') return 'info';
    if (r === 'medium') return 'warn';
    return 'error';
  }

  getInsightIcon(rating: string): string {
    const t = this.getInsightType(rating);
    if (t === 'ok') return '✓';
    if (t === 'warn' || t === 'error') return '!';
    return 'i';
  }

  getCfrInsightText(): string {
    if (!this.metrics) return '';
    const r = this.metrics.changeFailureRating.toLowerCase();
    if (r === 'elite') return 'Excellent stability — fewer than 5% of deployments cause regressions. Your automated test coverage is working.';
    if (r === 'high') return 'Failure rate within acceptable bounds. Adding integration tests to pre-production gates can push you into Elite.';
    if (r === 'medium') return 'Moderate failure rate detected. Review recent rollback patterns and identify recurring failure sources.';
    return 'High change failure rate requires immediate attention. Audit pipeline configurations and consider feature flags for risky changes.';
  }

  getLeadTimeInsightText(): string {
    if (!this.metrics) return '';
    const r = this.metrics.leadTimeRating.toLowerCase();
    if (r === 'elite') return 'Commits are reaching production in under an hour. Your CI/CD pipeline is well-optimized — maintain trunk-based development.';
    if (r === 'high') return 'Lead time under one day is strong. Parallelizing pipeline stages or caching dependencies could reduce it further.';
    if (r === 'medium') return 'Lead time between 1–7 days suggests manual approval bottlenecks or slow test suites. Automate PR review gates where possible.';
    return 'Extended lead times are limiting delivery velocity. Investigate staging deployment bottlenecks and manual approval queues.';
  }

  getMttrInsightText(): string {
    if (!this.metrics) return '';
    const r = this.metrics.mttrRating.toLowerCase();
    if (r === 'elite') return 'Service is restored in under an hour. Automated rollback triggers and monitoring alerts are functioning well.';
    if (r === 'high') return 'Good recovery time. Improving incident runbooks and on-call handoffs could help achieve Elite recovery speeds.';
    if (r === 'medium') return 'Recovery averaging days. Invest in better observability tooling and automated anomaly detection to cut detection lag.';
    return 'Critical: slow recovery times indicate missing runbooks and alerting. Establish automated rollback procedures immediately.';
  }

  getReworkInsightText(): string {
    if (!this.metrics) return '';
    const r = this.metrics.reworkRateRating.toLowerCase();
    if (r === 'elite') return 'Under 2% code rework — your team is shipping stable, well-tested features consistently. Keep up the PR quality standards.';
    if (r === 'high') return 'Rework within acceptable range. Pair programming and design reviews at the planning stage can reduce hotfixes further.';
    if (r === 'medium') return 'Elevated rework rate. Identify whether root cause is insufficient QA coverage or unclear requirements during planning.';
    return 'High rework rate signals systemic quality issues. Mandatory peer reviews and definition-of-done checklists are recommended.';
  }

  getReworkFillWidth(): string {
    const value = this.metrics ? Math.min(this.metrics.reworkRate, 100) : 0;
    return value + '%';
  }

  getOverallScore(): number {
    const scores = this.getMetricScores();
    if (scores.length === 0) return 0;
    return Math.round(scores.reduce((acc, s) => acc + s.score, 0) / scores.length);
  }

  getOverallScoreLabel(): string {
    const s = this.getOverallScore();
    if (s >= 90) return 'Elite';
    if (s >= 60) return 'High';
    if (s >= 35) return 'Medium';
    return 'Low';
  }

  getOverallScoreClass(): string {
    return 'grade--' + this.getOverallScoreLabel().toLowerCase();
  }

  /** Percent progress toward Elite benchmark (0–100). Used for the per-card progress track. */
  getEliteProgressPct(rating: string): number {
    const r = (rating || '').toLowerCase();
    if (r === 'elite') return 100;
    if (r === 'high')  return 68;
    if (r === 'medium') return 38;
    return 14;
  }

  getPriorityActions(): { metric: string; value: string; rating: string; action: string; cls: string }[] {
    if (!this.metrics) return [];
    const all = [
      { metric: 'Change Failure Rate', value: `${this.metrics.changeFailureRate.toFixed(1)}%`, rating: this.metrics.changeFailureRating, action: this.getCfrInsightText(), cls: this.getRatingBadgeClass(this.metrics.changeFailureRating) },
      { metric: 'Lead Time for Changes', value: `${this.formatLeadTime(this.metrics.leadTimeForChangesHours)} ${this.leadTimeUnit(this.metrics.leadTimeForChangesHours)}`, rating: this.metrics.leadTimeRating, action: this.getLeadTimeInsightText(), cls: this.getRatingBadgeClass(this.metrics.leadTimeRating) },
      { metric: 'Mean Time to Restore', value: `${this.formatMttr(this.metrics.meanTimeToRestoreHours)} ${this.mttrUnit(this.metrics.meanTimeToRestoreHours)}`, rating: this.metrics.mttrRating, action: this.getMttrInsightText(), cls: this.getRatingBadgeClass(this.metrics.mttrRating) },
      { metric: 'Deployment Frequency', value: `${this.metrics.deploymentFrequency.toFixed(1)}/day`, rating: this.metrics.deploymentFrequencyRating, action: 'Increase cadence by decomposing large releases into smaller, independently deployable increments.', cls: this.getRatingBadgeClass(this.metrics.deploymentFrequencyRating) },
      { metric: 'Rework Rate', value: `${this.metrics.reworkRate.toFixed(1)}%`, rating: this.metrics.reworkRateRating, action: this.getReworkInsightText(), cls: this.getRatingBadgeClass(this.metrics.reworkRateRating) },
    ];
    const order: Record<string, number> = { low: 0, medium: 1, high: 2, elite: 3 };
    const sorted = all.sort((a, b) => (order[a.rating.toLowerCase()] ?? 4) - (order[b.rating.toLowerCase()] ?? 4));
    const nonElite = sorted.filter(a => a.rating.toLowerCase() !== 'elite');
    return (nonElite.length > 0 ? nonElite : sorted).slice(0, 3);
  }

  private ratingToScore(rating: string): number {
    const r = (rating || '').toLowerCase();
    if (r === 'elite') return 100;
    if (r === 'high') return 72;
    if (r === 'medium') return 44;
    return 18;
  }
}
