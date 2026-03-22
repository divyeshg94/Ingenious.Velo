import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface PipelineRunDto {
  id: string;
  orgId: string;
  projectId: string;
  adoPipelineId: number;
  pipelineName: string;
  runNumber: string;
  result: string;
  startTime: string;
  finishTime?: string;
  durationMs?: number;
  isDeployment: boolean;
  stageName?: string;
  triggeredBy?: string;
  ingestedAt: string;
}

@Injectable({ providedIn: 'root' })
export class PipelineService {
  private apiUrl = `${environment.apiUrl}/api/pipelines`;

  constructor(private http: HttpClient) {}

  getRuns(projectId: string, page = 1, pageSize = 50): Observable<PipelineRunDto[]> {
    return this.http.get<PipelineRunDto[]>(this.apiUrl, {
      params: { projectId, page: page.toString(), pageSize: pageSize.toString() }
    });
  }
}
