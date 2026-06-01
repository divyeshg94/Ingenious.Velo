import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface DoraMetricsDto {
  id: string;
  orgId: string;
  projectId: string;
  computedAt: string;
  periodStart: string;
  periodEnd: string;
  deploymentFrequency: number;
  deploymentFrequencyRating: string;
  /** True when no deployment-tagged pipelines were detected; metric is an estimate from all successful runs. */
  isDeploymentFrequencyEstimated: boolean;
  leadTimeForChangesHours: number;
  leadTimeRating: string;
  /** Always true: Lead Time is currently average pipeline build duration, not PR-merge-to-deploy time. */
  isLeadTimeApproximate: boolean;
  changeFailureRate: number;
  changeFailureRating: string;
  /** True when no deployment-tagged pipelines were detected; CFR is estimated from all pipeline runs. */
  isChangeFailureRateEstimated: boolean;
  meanTimeToRestoreHours: number;
  mttrRating: string;
  /** True when no deployment-tagged pipelines were detected; MTTR is estimated from all pipeline runs. */
  isMttrEstimated: boolean;
  reworkRate: number;
  reworkRateRating: string;
  /** True when no work-item events were available; Rework Rate defaulted to 0 (insufficient data). */
  isReworkRateEstimated: boolean;
}

@Injectable({ providedIn: 'root' })
export class DoraMetricsService {
  private apiUrl = `${environment.apiUrl}/api/dora`;

  constructor(private http: HttpClient) {
    console.log('[DoraMetricsService] Initialized with API URL:', this.apiUrl);
  }

  getLatestMetrics(projectId: string, repositoryName?: string): Observable<DoraMetricsDto> {
    let url = `${this.apiUrl}/latest?projectId=${encodeURIComponent(projectId)}`;
    if (repositoryName) url += `&repositoryName=${encodeURIComponent(repositoryName)}`;
    console.log('[DoraMetricsService] GET request:', url);
    return this.http.get<DoraMetricsDto>(url);
  }

  getMetricsHistory(projectId: string, days: number = 30, repositoryName?: string): Observable<DoraMetricsDto[]> {
    let url = `${this.apiUrl}/history?projectId=${encodeURIComponent(projectId)}&days=${days}`;
    if (repositoryName) url += `&repositoryName=${encodeURIComponent(repositoryName)}`;
    console.log('[DoraMetricsService] GET request:', url);
    return this.http.get<DoraMetricsDto[]>(url);
  }
}
