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
  leadTimeForChangesHours: number;
  leadTimeRating: string;
  changeFailureRate: number;
  changeFailureRating: string;
  meanTimeToRestoreHours: number;
  mttrRating: string;
  reworkRate: number;
  reworkRateRating: string;
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
