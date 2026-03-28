import { Component, OnInit, OnDestroy } from '@angular/core';
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
  selector: 'velo-dora',
  standalone: true,
  imports: [CommonModule, FormsModule],
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
  repositories: string[] = [];
  teamMappings: TeamMappingDto[] = [];
  selectedRepository: string | null = null;

  private pollTimer: ReturnType<typeof setInterval> | null = null;
  private pollAttempts = 0;
  private readonly MAX_POLL_ATTEMPTS = 12; // 12 × 5s = 60s max

  readonly periods = [
    { label: 'Last 30 days', days: 30 },
    { label: 'Last 90 days', days: 90 },
    { label: '1 year', days: 365 },
  ];

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
        }
      } catch {}
    }

    if (this.selectedProjectId) {
      this.loadRepositories();
      this.loadHistory();
    }
  }

  ngOnDestroy(): void {
    this.stopPolling();
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
    this.loadHistory();
  }

  getTeamLabel(repo: string): string {
    const mapping = this.teamMappings.find(m => m.repositoryName === repo);
    return mapping ? `${mapping.teamName} (${repo})` : repo;
  }

  loadHistory(): void {
    if (!this.selectedProjectId) {
      this.errorMessage = 'Please select a project in the Connections tab first.';
      return;
    }

    this.isLoading = true;
    this.errorMessage = '';
    this.gatheringMessage = '';

    this.doraService.getMetricsHistory(this.selectedProjectId, this.selectedDays, this.selectedRepository ?? undefined).subscribe({
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

      this.doraService.getMetricsHistory(this.selectedProjectId!, this.selectedDays, this.selectedRepository ?? undefined).subscribe({
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

  // Returns bar heights (15–95) for sparkline, oldest → newest, always 7 bars.
  // ratingField is used as a fallback when all values are identical (range === 0),
  // so bars render at a meaningful height instead of the minimum 15%.
  getSparkHeights(field: keyof DoraMetricsDto, ratingField?: keyof DoraMetricsDto): number[] {
    if (this.history.length === 0) {
      return [40, 50, 60, 65, 75, 80, 95];
    }

    const pts = [...this.history].reverse().slice(-7);
    const values = pts.map(m => m[field] as number);
    const min = Math.min(...values);
    const max = Math.max(...values);
    const range = max - min;

    let heights: number[];

    if (range === 0) {
      // All values are identical — use the latest rating to determine a stable bar height
      // so the sparkline shows meaningful context instead of all-minimum bars.
      const latestRating = ratingField ? (pts[pts.length - 1][ratingField] as string) : '';
      const stableHeight = this.ratingToSparkHeight(latestRating);
      heights = pts.map(() => stableHeight);
    } else {
      heights = values.map(v => Math.round(((v - min) / range) * 80 + 15));
    }

    // Pad left with the oldest bar's height so there are always 7 bars shown,
    // even when fewer than 7 history snapshots exist.
    while (heights.length < 7) heights.unshift(heights[0]);

    return heights;
  }

  /** Maps a DORA rating string to a sparkline bar height (15–95 scale). */
  private ratingToSparkHeight(rating: string): number {
    const r = (rating || '').toLowerCase();
    if (r === 'elite')  return 85;
    if (r === 'high')   return 65;
    if (r === 'medium') return 42;
    return 20; // low
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

  /** Safe string cast for template binding (avoids pipe usage on union types). */
  asString(val: string | number | boolean | null | undefined): string {
    return String(val ?? '');
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

  // ── Chart helpers ──────────────────────────────────────────

  /** SVG chart dimensions (viewBox: "0 0 600 130"). */
  private readonly CHART_W = 560;
  private readonly CHART_H = 100;
  private readonly CHART_X = 20;
  private readonly CHART_Y = 10;

  readonly chartMetrics: { field: keyof DoraMetricsDto; ratingField: keyof DoraMetricsDto; label: string; color: string }[] = [
    { field: 'deploymentFrequency',    ratingField: 'deploymentFrequencyRating', label: 'Deploy Freq',   color: '#22c55e' },
    { field: 'leadTimeForChangesHours', ratingField: 'leadTimeRating',           label: 'Lead Time',     color: '#3b82f6' },
    { field: 'changeFailureRate',       ratingField: 'changeFailureRating',      label: 'Failure Rate',  color: '#ef4444' },
    { field: 'meanTimeToRestoreHours',  ratingField: 'mttrRating',               label: 'MTTR',          color: '#f59e0b' },
    { field: 'reworkRate',              ratingField: 'reworkRateRating',         label: 'Rework Rate',   color: '#8b5cf6' },
  ];

  /**
   * Generates SVG polyline `points` string for a given metric across the history.
   * Uses the rating score (0–100) so all 5 metrics share the same Y-scale.
   */
  getChartPoints(ratingField: keyof DoraMetricsDto): string {
    const pts = [...this.history].reverse(); // oldest → newest
    if (pts.length < 2) return '';
    const n = pts.length;
    return pts.map((m, i) => {
      const score = this.ratingToScore(m[ratingField] as string);
      const x = this.CHART_X + (i / (n - 1)) * this.CHART_W;
      const y = this.CHART_Y + this.CHART_H - (score / 100) * this.CHART_H;
      return `${x.toFixed(1)},${y.toFixed(1)}`;
    }).join(' ');
  }

  /** SVG `cx` coordinate for the latest (rightmost) data point. */
  getChartLatestX(): number {
    return this.CHART_X + this.CHART_W; // rightmost
  }

  /** SVG `cy` coordinate for the latest data point of a given metric. */
  getChartLatestY(ratingField: keyof DoraMetricsDto): number {
    if (!this.latest) return this.CHART_Y + this.CHART_H / 2;
    const score = this.ratingToScore(this.latest[ratingField] as string);
    return this.CHART_Y + this.CHART_H - (score / 100) * this.CHART_H;
  }

  /** Y-axis grid line positions (scores: 0, 18, 44, 72, 100). */
  getChartGridLines(): { y: number; label: string }[] {
    return [
      { y: this.CHART_Y + this.CHART_H,                                    label: 'Low' },
      { y: this.CHART_Y + this.CHART_H - (44 / 100) * this.CHART_H,       label: 'Medium' },
      { y: this.CHART_Y + this.CHART_H - (72 / 100) * this.CHART_H,       label: 'High' },
      { y: this.CHART_Y,                                                    label: 'Elite' },
    ];
  }

  /** Number of metrics that improved since the previous period. */
  getImprovementCount(): number {
    if (!this.latest || !this.previous) return 0;
    let count = 0;
    if (this.isImprovement('deploymentFrequency', false))    count++;
    if (this.isImprovement('leadTimeForChangesHours', true)) count++;
    if (this.isImprovement('changeFailureRate', true))       count++;
    if (this.isImprovement('meanTimeToRestoreHours', true))  count++;
    if (this.isImprovement('reworkRate', true))              count++;
    return count;
  }

  getImprovementChipClass(): string {
    const c = this.getImprovementCount();
    if (c >= 4) return 'velocity--excellent';
    if (c >= 2) return 'velocity--good';
    if (c >= 1) return 'velocity--fair';
    return 'velocity--none';
  }

  private ratingToScore(rating: string): number {
    const r = (rating || '').toLowerCase();
    if (r === 'elite')  return 100;
    if (r === 'high')   return 72;
    if (r === 'medium') return 44;
    return 18;
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
