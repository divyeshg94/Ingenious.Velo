import { Injectable } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface OrgConnectionDto {
  orgId: string;
  orgUrl: string;
  displayName: string;
  isPremium: boolean;
  dailyTokenBudget?: number;
  registerAt?: string;
  lastSyncedAt?: string;
}

/** Response from POST /api/orgs/connect — wraps the org plus an auto-sync flag. */
export interface ConnectOrgResponse {
  org: OrgConnectionDto;
  autoSyncTriggered: boolean;
}

@Injectable({ providedIn: 'root' })
export class OrgConnectionService {
  private apiUrl = `${environment.apiUrl}/api/orgs`;

  constructor(private http: HttpClient) {
    console.log('[OrgConnectionService] Initialized with API URL:', this.apiUrl);
  }

  /**
   * Get current organization
   * AUTO-DETECTS from JWT token context
   * In Azure DevOps: org_id from Azure AD B2C token
   * In local dev: org_id from mock token
   */
  getMyOrganization(): Observable<OrgConnectionDto> {
    const url = `${this.apiUrl}/me`;
    console.log('[OrgConnectionService] GET /me - Auto-detecting organization');
    return this.http.get<OrgConnectionDto>(url);
  }

  /**
   * Get available projects for current organization
   * Projects populate automatically as pipelines execute
   */
  getAvailableProjects(): Observable<string[]> {
    const url = `${this.apiUrl}/projects`;
    console.log('[OrgConnectionService] GET /projects - Fetching available projects');
    return this.http.get<string[]>(url);
  }

  /**
   * Connect organization by URL.
   * Optionally pass the ADO access token so the API can trigger a background
   * historical backfill immediately after registration.
   */
  connectOrganization(orgUrl: string, adoToken?: string): Observable<ConnectOrgResponse> {
    const url = `${this.apiUrl}/connect`;
    console.log('[OrgConnectionService] POST /connect - Connecting organization', { orgUrl, hasToken: !!adoToken });

    const headers = adoToken
      ? new HttpHeaders({ 'X-Ado-Access-Token': adoToken })
      : undefined;

    return this.http.post<ConnectOrgResponse>(url, { orgUrl }, { headers });
  }

  /**
   * Update organization details
   * Optional - org is already auto-detected
   * Use only to customize org URL or display name
   */
  updateOrganization(orgUrl: string, displayName?: string): Observable<OrgConnectionDto> {
    const url = `${this.apiUrl}/update`;
    const payload = { orgUrl, displayName };
    console.log('[OrgConnectionService] POST /update - Updating organization', payload);
    return this.http.post<OrgConnectionDto>(url, payload);
  }
}
