import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
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

interface TeamHealthApiResponse {
  status?: string;
  message?: string;
  note?: string;
  health?: TeamHealthDto | null;
}

@Injectable({ providedIn: 'root' })
export class TeamHealthService {
  private readonly apiUrl = `${environment.apiUrl}/api/health`;

  constructor(private http: HttpClient) {}

  /** Fetch the latest team health snapshot for a project (auto-computes if none exists). */
  getTeamHealth(projectId: string, repositoryName?: string): Observable<TeamHealthDto> {
    let url = `${this.apiUrl}/team?projectId=${encodeURIComponent(projectId)}`;
    if (repositoryName) url += `&repositoryName=${encodeURIComponent(repositoryName)}`;
    return this.http.get<TeamHealthDto | TeamHealthApiResponse>(url).pipe(
      map(response => this.unwrapTeamHealthResponse(response, 'Team health data is not available yet.'))
    );
  }

  /** Force a fresh recomputation. Called when the user clicks "Refresh". */
  recompute(projectId: string): Observable<TeamHealthDto> {
    return this.http.post<TeamHealthDto | TeamHealthApiResponse>(
      `${this.apiUrl}/recompute?projectId=${encodeURIComponent(projectId)}`,
      {}
    ).pipe(
      map(response => this.unwrapTeamHealthResponse(response, 'Team health recompute did not return data.'))
    );
  }

  private unwrapTeamHealthResponse(
    response: TeamHealthDto | TeamHealthApiResponse,
    fallbackMessage: string
  ): TeamHealthDto {
    if (this.isTeamHealthDto(response)) {
      return response;
    }

    if (response.status === 'ok' && response.health && this.isTeamHealthDto(response.health)) {
      return response.health;
    }

    if (response.status === 'empty') {
      throw new Error(response.note || 'No mapped repositories found for this team/project yet.');
    }

    throw new Error(response.message || response.note || fallbackMessage);
  }

  private isTeamHealthDto(value: unknown): value is TeamHealthDto {
    if (!value || typeof value !== 'object') {
      return false;
    }

    const dto = value as TeamHealthDto;
    return typeof dto.projectId === 'string' && typeof dto.computedAt === 'string';
  }
}
