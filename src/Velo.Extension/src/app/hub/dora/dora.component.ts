import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { DoraMetricsService, DoraMetricsDto } from '../../shared/services/dora-metrics.service';
import { getSDK, isRunningInADO } from '../../shared/services/sdk-initializer.service';

interface MetricScore {
  label: string;
  rating: string;
  score: number;
  cls: string;
}

@Component({
  selector: 'velo-dora',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './dora.component.html',
  styleUrls: ['./dora.component.scss']
})
export class DoraComponent implements OnInit, OnDestroy {
  history: DoraMetricsDto[] = [];
  isLoading = false;
  errorMessage = '';
  gatheringMessage = '';
  /** True while a background sync is in progress — drives the syncing spinner in the template. */
  isSyncing = false;
  selectedProjectId: string | null = null;
  selectedDays = 90;

  private pollTimer: ReturnType<typeof setInterval> | null = null;
  private pollAttempts = 0;
  private readonly MAX_POLL_ATTEMPTS = 12; // 12 × 5s = 60s max

  readonly periods = [
    { label: 'Last 30 days', days: 30 },
    { label: 'Last 90 days', days: 90 },
    { label: '1 year', days: 365 },
  ];

  constructor(private doraService: DoraMetricsService) {}

  ngOnInit(): void {
    this.selectedProjectId = sessionStorage.getItem('selectedProjectId');

    if (!this.selectedProjectId && isRunningInADO()) {
      try {
        const SDK = getSDK();
        const webContext = SDK.getWebContext?.();
        if (webContext?.project?.name) {
          this.selectedProjectId = webContext.project.name;
          sessionStorage.setItem('selectedProjectId', this.selectedProjectId!);
        }
      } catch {}
    }

    if (this.selectedProjectId) {
      this.loadHistory();
    }
  }

  ngOnDestroy(): void {
    this.stopPolling();
  }

  loadHistory(): void {
    if (!this.selectedProjectId) {
      this.errorMessage = 'Please select a project in the Connections tab first.';
      return;
    }

    this.isLoading = true;
    this.errorMessage = '';
    this.gatheringMessage = '';

    this.doraService.getMetricsHistory(this.selectedProjectId, this.selectedDays).subscribe({
      next: (data: any) => {
        this.isLoading = false;

        if (Array.isArray(data) && data.length > 0) {
          // Got real data — cancel any in-progress polling
          this.isSyncing = false;
          this.stopPolling();
          this.history = data.sort((a: DoraMetricsDto, b: DoraMetricsDto) =>
            new Date(b.computedAt).getTime() - new Date(a.computedAt).getTime()
          );
        } else {
          // Empty history — call getLatestMetrics which will:
          //   • Return { status: 'syncing' } AND kick off a background sync if ADO token is present
          //   • Return { status: 'gathering' } when no ADO token is available
          this.history = [];
          this.triggerAutoRecovery();
        }
      },
      error: (err) => {
        this.isLoading = false;
        this.errorMessage = err.message || 'Failed to load DORA history. Please check your connection and try again.';
      }
    });
  }

  /**
   * Calls GET /api/dora/latest which auto-triggers a background sync (via X-Ado-Access-Token
   * injected by the auth interceptor) when no metrics exist. Handles three states:
   *   'syncing'   — sync started, poll every 5 s until data appears (max 60 s)
   *   'gathering' — no ADO token available, show static waiting message
   *   real DTO    — metrics already exist (edge case), refresh history
   */
  private triggerAutoRecovery(): void {
    if (!this.selectedProjectId) return;

    this.doraService.getLatestMetrics(this.selectedProjectId).subscribe({
      next: (response: any) => {
        if (response?.status === 'syncing') {
          this.isSyncing = true;
          this.gatheringMessage = response.message;
          this.startPolling();
        } else if (response?.status === 'gathering') {
          this.isSyncing = false;
          this.gatheringMessage = response.message;
        } else if (response?.id) {
          // Rare: latest exists but history was empty (period filter mismatch)
          this.isSyncing = false;
          this.loadHistory();
        }
      },
      error: () => {
        this.gatheringMessage = 'No pipeline data yet. Metrics will appear after your first pipeline run.';
      }
    });
  }

  private startPolling(): void {
    if (this.pollTimer) return; // already polling
    this.pollAttempts = 0;

    this.pollTimer = setInterval(() => {
      this.pollAttempts++;

      if (this.pollAttempts > this.MAX_POLL_ATTEMPTS) {
        this.stopPolling();
        this.isSyncing = false;
        this.gatheringMessage = 'Sync is taking longer than expected. The page will update automatically once data is ready.';
        return;
      }

      this.doraService.getMetricsHistory(this.selectedProjectId!, this.selectedDays).subscribe({
        next: (data: any) => {
          if (Array.isArray(data) && data.length > 0) {
            this.isSyncing = false;
            this.gatheringMessage = '';
            this.stopPolling();
            this.history = data.sort((a: DoraMetricsDto, b: DoraMetricsDto) =>
              new Date(b.computedAt).getTime() - new Date(a.computedAt).getTime()
            );
          }
        },
        error: () => { /* silent — keep polling */ }
      });
    }, 5_000); // check every 5 seconds
  }

  private stopPolling(): void {
    if (this.pollTimer) {
      clearInterval(this.pollTimer);
      this.pollTimer = null;
    }
  }

  setDays(days: number): void {
    if (this.selectedDays === days) return;
    this.selectedDays = days;
    this.stopPolling();
    this.isSyncing = false;
    this.loadHistory();
  }

  get latest(): DoraMetricsDto | null {
    return this.history[0] ?? null;
  }

  get previous(): DoraMetricsDto | null {
    return this.history[1] ?? null;
  }

  getDelta(field: keyof DoraMetricsDto): number {
    if (!this.latest || !this.previous) return 0;
    return (this.latest[field] as number) - (this.previous[field] as number);
  }

  isImprovement(field: keyof DoraMetricsDto, lowerIsBetter: boolean): boolean {
    const delta = this.getDelta(field);
    if (delta === 0) return false;
    return lowerIsBetter ? delta < 0 : delta > 0;
  }

  // Returns bar heights (15–95) for sparkline, oldest → newest, capped at 7 points
  getSparkHeights(field: keyof DoraMetricsDto): number[] {
    if (this.history.length === 0) {
      return [40, 50, 60, 65, 75, 80, 95];
    }
    const pts = [...this.history].reverse().slice(-7);
    const values = pts.map(m => m[field] as number);
    const min = Math.min(...values);
    const max = Math.max(...values);
    const range = max - min || 1;
    return values.map(v => Math.round(((v - min) / range) * 80 + 15));
  }

  formatDelta(delta: number, unit: string): string {
    const abs = Math.abs(delta);
    if (unit === 'hours') {
      return abs < 1 ? `${Math.round(abs * 60)}m` : `${abs.toFixed(1)}h`;
    }
    return `${abs.toFixed(1)}${unit}`;
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

  getOverallHealthLabel(): string {
    if (!this.latest) return '';
    const ratings = [
      this.latest.deploymentFrequencyRating,
      this.latest.leadTimeRating,
      this.latest.changeFailureRating,
      this.latest.mttrRating,
      this.latest.reworkRateRating,
    ].map(r => r.toLowerCase());
    const elite = ratings.filter(r => r === 'elite').length;
    const high = ratings.filter(r => r === 'high').length;
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

  getMetricScores(): MetricScore[] {
    if (!this.latest) return [];
    const ratingToScore = (r: string) => {
      const k = r.toLowerCase();
      if (k === 'elite') return 100;
      if (k === 'high') return 72;
      if (k === 'medium') return 44;
      return 18;
    };
    return [
      { label: 'Deploy Freq',  rating: this.latest.deploymentFrequencyRating, score: ratingToScore(this.latest.deploymentFrequencyRating), cls: this.getRatingBadgeClass(this.latest.deploymentFrequencyRating) },
      { label: 'Lead Time',    rating: this.latest.leadTimeRating,             score: ratingToScore(this.latest.leadTimeRating),             cls: this.getRatingBadgeClass(this.latest.leadTimeRating) },
      { label: 'Failure Rate', rating: this.latest.changeFailureRating,        score: ratingToScore(this.latest.changeFailureRating),        cls: this.getRatingBadgeClass(this.latest.changeFailureRating) },
      { label: 'MTTR',         rating: this.latest.mttrRating,                 score: ratingToScore(this.latest.mttrRating),                 cls: this.getRatingBadgeClass(this.latest.mttrRating) },
      { label: 'Rework Rate',  rating: this.latest.reworkRateRating,           score: ratingToScore(this.latest.reworkRateRating),           cls: this.getRatingBadgeClass(this.latest.reworkRateRating) },
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

  getTrendInsightText(): string {
    if (!this.latest || !this.previous) return 'Only one data snapshot available. Check back after the next metrics computation cycle for trend analysis.';
    const improved: string[] = [];
    const regressed: string[] = [];

    if (this.isImprovement('deploymentFrequency', false)) improved.push('Deployment Frequency');
    else if (this.getDelta('deploymentFrequency') < 0) regressed.push('Deployment Frequency');

    if (this.isImprovement('leadTimeForChangesHours', true)) improved.push('Lead Time');
    else if (this.getDelta('leadTimeForChangesHours') > 0) regressed.push('Lead Time');

    if (this.isImprovement('changeFailureRate', true)) improved.push('Change Failure Rate');
    else if (this.getDelta('changeFailureRate') > 0) regressed.push('Change Failure Rate');

    if (this.isImprovement('meanTimeToRestoreHours', true)) improved.push('MTTR');
    else if (this.getDelta('meanTimeToRestoreHours') > 0) regressed.push('MTTR');

    if (improved.length > 0 && regressed.length === 0) {
      return `All tracked metrics are trending positively. ${improved.join(', ')} improved since the previous period.`;
    }
    if (improved.length > 0) {
      return `${improved.join(', ')} improved since the previous period. Watch ${regressed.join(', ')} which trended unfavorably.`;
    }
    if (regressed.length > 0) {
      return `${regressed.join(', ')} regressed since the previous period. Review pipeline changes and deployment patterns from this window.`;
    }
    return 'Metrics are stable compared to the previous period. No significant changes detected.';
  }
}
