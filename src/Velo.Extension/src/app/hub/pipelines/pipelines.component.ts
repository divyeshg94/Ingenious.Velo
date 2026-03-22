import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { PipelineService, PipelineRunDto } from '../../shared/services/pipeline.service';

@Component({
  selector: 'velo-pipelines',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './pipelines.component.html',
  styleUrls: ['./pipelines.component.scss']
})
export class PipelinesComponent implements OnInit {
  runs: PipelineRunDto[] = [];
  isLoading = false;
  errorMessage = '';
  selectedProjectId: string | null = null;
  showDeploymentsOnly = false;
  page = 1;
  pageSize = 50;
  hasMore = false;

  get filteredRuns(): PipelineRunDto[] {
    return this.showDeploymentsOnly ? this.runs.filter(r => r.isDeployment) : this.runs;
  }

  constructor(private pipelineService: PipelineService) {}

  ngOnInit(): void {
    this.selectedProjectId = sessionStorage.getItem('selectedProjectId');
    if (this.selectedProjectId) {
      this.loadRuns();
    }
  }

  loadRuns(): void {
    if (!this.selectedProjectId) return;
    this.isLoading = true;
    this.errorMessage = '';

    this.pipelineService.getRuns(this.selectedProjectId, this.page, this.pageSize).subscribe({
      next: (runs) => {
        this.runs = runs;
        this.hasMore = runs.length === this.pageSize;
        this.isLoading = false;
      },
      error: (err) => {
        this.errorMessage = err.error?.message || err.message || 'Failed to load pipeline runs.';
        this.isLoading = false;
      }
    });
  }

  loadMore(): void {
    if (!this.selectedProjectId || !this.hasMore) return;
    this.page++;
    this.isLoading = true;

    this.pipelineService.getRuns(this.selectedProjectId, this.page, this.pageSize).subscribe({
      next: (runs) => {
        this.runs = [...this.runs, ...runs];
        this.hasMore = runs.length === this.pageSize;
        this.isLoading = false;
      },
      error: (err) => {
        this.errorMessage = err.error?.message || err.message || 'Failed to load more runs.';
        this.isLoading = false;
        this.page--;
      }
    });
  }

  refresh(): void {
    this.page = 1;
    this.runs = [];
    this.loadRuns();
  }

  formatDuration(ms?: number): string {
    if (!ms) return '—';
    const s = Math.floor(ms / 1000);
    if (s < 60) return `${s}s`;
    const m = Math.floor(s / 60);
    const rem = s % 60;
    if (m < 60) return `${m}m ${rem}s`;
    const h = Math.floor(m / 60);
    return `${h}h ${m % 60}m`;
  }

  getResultClass(result: string): string {
    switch ((result || '').toLowerCase()) {
      case 'succeeded': return 'result-success';
      case 'failed': return 'result-failed';
      case 'partiallysucceeded': return 'result-partial';
      case 'canceled': return 'result-canceled';
      default: return 'result-unknown';
    }
  }

  getResultLabel(result: string): string {
    switch ((result || '').toLowerCase()) {
      case 'succeeded': return 'Passed';
      case 'failed': return 'Failed';
      case 'partiallysucceeded': return 'Partial';
      case 'canceled': return 'Canceled';
      default: return result || 'Unknown';
    }
  }
}
