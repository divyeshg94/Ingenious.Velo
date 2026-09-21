import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface CreateShareRequest {
  projectId: string;
  repositoryName?: string;
}

export interface CreateShareResponse {
  id: string;
  shareUrl: string;
  expiresAt: string;
}

@Injectable({ providedIn: 'root' })
export class ShareService {
  private apiUrl = `${environment.apiUrl}/api/share`;

  constructor(private http: HttpClient) {}

  createDoraShare(request: CreateShareRequest): Observable<CreateShareResponse> {
    return this.http.post<CreateShareResponse>(`${this.apiUrl}/dora`, request);
  }

  revokeDoraShare(id: string): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.apiUrl}/dora/${id}/revoke`, {});
  }

  /**
   * Downloads DORA history as CSV. Goes through HttpClient (not a bare `window.open`)
   * so the auth interceptor attaches the Bearer token — a raw navigation to the API
   * wouldn't carry it and would just get a 401.
   */
  exportDoraCsv(projectId: string, days: number, repositoryName?: string): Observable<Blob> {
    const params: Record<string, string> = { projectId, days: String(days) };
    if (repositoryName) params['repositoryName'] = repositoryName;
    return this.http.get(`${environment.apiUrl}/api/dora/export`, { params, responseType: 'blob' });
  }
}
