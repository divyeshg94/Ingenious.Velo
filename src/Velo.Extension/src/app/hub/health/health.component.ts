import { Component, OnInit } from '@angular/core';
import { CommonModule, DecimalPipe, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TeamHealthService, TeamHealthDto } from '../../shared/services/team-health.service';
import { TeamMappingService, TeamMappingDto } from '../../shared/services/team-mapping.service';
import { isRunningInADO, getSDK } from '../../shared/services/sdk-initializer.service';

@Component({
  selector: 'velo-health',
  standalone: true,
  imports: [CommonModule, DecimalPipe, DatePipe, FormsModule],
  templateUrl: './health.component.html',
  styleUrls: ['./health.component.scss'],
})
export class HealthComponent implements OnInit {
  health: TeamHealthDto | null = null;
  isLoading = false;
  isRecomputing = false;
  errorMessage = '';
  selectedProjectId: string | null = null;
  repositories: string[] = [];
  teamMappings: TeamMappingDto[] = [];
  selectedRepository: string | null = null;

  constructor(private healthService: TeamHealthService, private teamMappingService: TeamMappingService) {}

  ngOnInit(): void {
    this.selectedProjectId = sessionStorage.getItem('selectedProjectId');
    if (!this.selectedProjectId && isRunningInADO()) {
      const ctx = getSDK()?.getWebContext?.();
      if (ctx?.project?.name) {
        this.selectedProjectId = ctx.project.name;
        sessionStorage.setItem('selectedProjectId', this.selectedProjectId!);
      }
    }
    if (this.selectedProjectId) {
      this.loadRepositories();
      this.load();
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
    this.load();
  }

  getTeamLabel(repo: string): string {
    const mapping = this.teamMappings.find(m => m.repositoryName === repo);
    return mapping ? `${mapping.teamName} (${repo})` : repo;
  }

  load(): void {
    if (!this.selectedProjectId) return;
    this.isLoading = true;
    this.errorMessage = '';
    this.healthService.getTeamHealth(this.selectedProjectId, this.selectedRepository ?? undefined).subscribe({
      next: (h) => { this.health = h; this.isLoading = false; },
      error: (err) => {
        this.errorMessage = err.message || 'Failed to load team health metrics.';
        this.isLoading = false;
      },
    });
  }

  refresh(): void {
    if (!this.selectedProjectId) return;
    this.isRecomputing = true;
    this.errorMessage = '';
    this.healthService.recompute(this.selectedProjectId).subscribe({
      next: (h) => { this.health = h; this.isRecomputing = false; },
      error: (err) => {
        this.errorMessage = err.message || 'Recompute failed.';
        this.isRecomputing = false;
      },
    });
  }

  // ── Score helpers ────────────────────────────────────────────────────

  /** Overall health score 0–100. Weighted average of quality signals. */
  getHealthScore(): number {
    if (!this.health) return 0;
    const { testPassRate, flakyTestRate, deploymentRiskScore, prApprovalRate } = this.health;
    // Pass rate (40%) + approval proxy (20%) + flaky inverse (20%) + risk inverse (20%)
    return Math.round(
      testPassRate * 0.40 +
      prApprovalRate * 0.20 +
      Math.max(0, 100 - flakyTestRate) * 0.20 +
      Math.max(0, 100 - deploymentRiskScore) * 0.20
    );
  }

  getHealthLabel(): string {
    const s = this.getHealthScore();
    if (s >= 85) return 'Excellent';
    if (s >= 70) return 'Good';
    if (s >= 50) return 'Fair';
    return 'Needs Attention';
  }

  getHealthClass(): string {
    const s = this.getHealthScore();
    if (s >= 85) return 'excellent';
    if (s >= 70) return 'good';
    if (s >= 50) return 'fair';
    return 'poor';
  }

  /** SVG arc for the health score ring (r=36, circumference≈226). */
  getScoreArc(): string {
    const pct = this.getHealthScore() / 100;
    const circ = 2 * Math.PI * 36;
    return `${circ * pct} ${circ * (1 - pct)}`;
  }

  // ── Cycle time helpers ───────────────────────────────────────────────

  formatHours(h: number): string {
    if (h <= 0) return '—';
    if (h < 1) return `${Math.round(h * 60)} min`;
    if (h < 24) return `${h.toFixed(1)} h`;
    return `${(h / 24).toFixed(1)} d`;
  }

  getTotalCycleHours(): number {
    if (!this.health) return 0;
    return this.health.codingTimeHours + this.health.reviewTimeHours +
           this.health.mergeTimeHours + this.health.deployTimeHours;
  }

  getCyclePct(hours: number): number {
    const total = this.getTotalCycleHours();
    return total > 0 ? Math.round(hours / total * 100) : 25;
  }

  // ── Quality metric helpers ───────────────────────────────────────────

  getPassClass(rate: number): string {
    if (rate >= 90) return 'excellent';
    if (rate >= 75) return 'good';
    if (rate >= 50) return 'fair';
    return 'poor';
  }

  getRiskClass(score: number): string {
    if (score <= 15) return 'excellent';
    if (score <= 35) return 'good';
    if (score <= 60) return 'fair';
    return 'poor';
  }

  getFlakyClass(rate: number): string {
    if (rate <= 5)  return 'excellent';
    if (rate <= 15) return 'good';
    if (rate <= 30) return 'fair';
    return 'poor';
  }
}
