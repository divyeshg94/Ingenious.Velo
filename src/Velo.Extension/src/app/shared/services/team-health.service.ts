import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface TeamHealthDto {
  id: string;
  orgId: string;
  projectId: string;
  computedAt: string;
  codingTimeHours: number;
  reviewTimeHours: number;
  mergeTimeHours: number;
  deployTimeHours: number;
  averagePrSizeLines: number;
  prCommentDensity: number;
  prApprovalRate: number;
  testPassRate: number;
  flakyTestRate: number;
  deploymentRiskScore: number;
}

@Injectable({ providedIn: 'root' })
export class TeamHealthService {
  private readonly apiUrl = `${environment.apiUrl}/api/health`;

  constructor(private http: HttpClient) {}

  /** Fetch the latest team health snapshot for a project (auto-computes if none exists). */
  getTeamHealth(projectId: string, repositoryName?: string): Observable<TeamHealthDto> {
    let url = `${this.apiUrl}?projectId=${encodeURIComponent(projectId)}`;
    if (repositoryName) url += `&repositoryName=${encodeURIComponent(repositoryName)}`;
    return this.http.get<TeamHealthDto>(url);
  }

  /** Force a fresh recomputation. Called when the user clicks "Refresh". */
  recompute(projectId: string): Observable<TeamHealthDto> {
    return this.http.post<TeamHealthDto>(
      `${this.apiUrl}/compute?projectId=${encodeURIComponent(projectId)}`,
      {}
    );
  }
}
