import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface SyncResult {
  ingested: number;
  metrics: any;
  orgId: string;
  projectId: string;
  syncedAt: string;
}

@Injectable({ providedIn: 'root' })
export class SyncService {
  private apiUrl = `${environment.apiUrl}/api/sync`;

  constructor(private http: HttpClient) {}

  syncProject(projectId: string): Observable<SyncResult> {
    return this.http.post<SyncResult>(`${this.apiUrl}/${encodeURIComponent(projectId)}`, {});
  }
}
