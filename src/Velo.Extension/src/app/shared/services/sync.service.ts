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
  /** Build webhook (build.complete) status */
  webhook: WebhookStatus;
  /** PR webhook (git.pullrequest.*) status */
  prWebhook?: WebhookStatus;
  /** Work item webhook (workitem.updated) status */
  workItemWebhook?: WebhookStatus;
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

  // ── Build webhook ──────────────────────────────────────────────────────────

  getHookStatus(projectId: string): Observable<WebhookStatus> {
    return this.http.get<WebhookStatus>(`${this.apiUrl}/hook-status/${encodeURIComponent(projectId)}`);
  }

  registerHook(projectId: string): Observable<WebhookStatus> {
    return this.http.post<WebhookStatus>(`${this.apiUrl}/hook/${encodeURIComponent(projectId)}`, {});
  }

  removeHook(subscriptionId: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/hook/${encodeURIComponent(subscriptionId)}`);
  }

  // ── PR webhook (git.pullrequest.created + git.pullrequest.updated) ─────────

  getPrHookStatus(projectId: string): Observable<WebhookStatus> {
    return this.http.get<WebhookStatus>(`${this.apiUrl}/pr-hook-status/${encodeURIComponent(projectId)}`);
  }

  registerPrHook(projectId: string): Observable<WebhookStatus> {
    return this.http.post<WebhookStatus>(`${this.apiUrl}/pr-hook/${encodeURIComponent(projectId)}`, {});
  }

  /** Reuse the same DELETE endpoint — subscription ID is generic for both build and PR hooks. */
  removePrHook(subscriptionId: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/hook/${encodeURIComponent(subscriptionId)}`);
  }

  // ── Work item webhook (workitem.updated → rework-rate tracking) ────────────

  getWorkItemHookStatus(projectId: string): Observable<WebhookStatus> {
    return this.http.get<WebhookStatus>(`${this.apiUrl}/workitem-hook-status/${encodeURIComponent(projectId)}`);
  }

  registerWorkItemHook(projectId: string): Observable<WebhookStatus> {
    return this.http.post<WebhookStatus>(`${this.apiUrl}/workitem-hook/${encodeURIComponent(projectId)}`, {});
  }

  /** Reuse the same DELETE endpoint — subscription ID is generic across all hook types. */
  removeWorkItemHook(subscriptionId: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/hook/${encodeURIComponent(subscriptionId)}`);
  }
}
