import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface WebhookStatus {
  isRegistered: boolean;
  subscriptionId?: string;
  webhookUrl?: string;
  projectId?: string;
  createdDate?: string;
  registrationError?: string;
  manualSetupUrl?: string;
}

export interface SyncResult {
  ingested: number;
  metrics: any;
  webhook: WebhookStatus;
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

  getHookStatus(projectId: string): Observable<WebhookStatus> {
    return this.http.get<WebhookStatus>(`${this.apiUrl}/hook-status/${encodeURIComponent(projectId)}`);
  }

  registerHook(projectId: string): Observable<WebhookStatus> {
    return this.http.post<WebhookStatus>(`${this.apiUrl}/hook/${encodeURIComponent(projectId)}`, {});
  }

  removeHook(subscriptionId: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/hook/${encodeURIComponent(subscriptionId)}`);
  }
}

