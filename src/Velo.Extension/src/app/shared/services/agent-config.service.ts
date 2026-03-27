import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface AgentConfigDto {
  id: string;
  orgId: string;
  foundryEndpoint: string;
  /** Optional — null means Velo will auto-create the agent on first chat. */
  agentId?: string;
  displayName?: string;
  /** Azure OpenAI model deployment name (e.g. "gpt-4o"). Used when auto-creating the agent. */
  deploymentName: string;
  isEnabled: boolean;
  // ── Auth option 1: API key ──────────────────────────────────────────────────
  /** Write-only: sent when saving. The server encrypts it before storage. Never returned by the server. */
  apiKey?: string;
  /** True when an encrypted API key is stored. Returned by server on GET. */
  hasApiKey: boolean;

  // ── Auth option 2: Service principal ────────────────────────────────────────
  /** Write-only: Azure AD Tenant ID. Never returned by the server. */
  tenantId?: string;
  /** Write-only: Service principal Client (App) ID. Never returned by the server. */
  clientId?: string;
  /** Write-only: Client secret (server encrypts before storage). Never returned by the server. */
  clientSecret?: string;
  /** True when encrypted service principal credentials are stored. Returned by server on GET. */
  hasServicePrincipal: boolean;
  updatedAt?: string;
}

export interface AgentConfigTestRequest {
  foundryEndpoint: string;
  /** Optional — null means Velo will auto-create the agent on first chat. */
  agentId?: string;
  /** Auth option 1 — used only during the test, never persisted. */
  apiKey?: string;
  /** Auth option 2 — used only during the test, never persisted. */
  tenantId?: string;
  clientId?: string;
  clientSecret?: string;
}

@Injectable({ providedIn: 'root' })
export class AgentConfigService {
  private readonly apiUrl = `${environment.apiUrl}/api/agentconfig`;

  constructor(private http: HttpClient) {}

  getConfig(): Observable<AgentConfigDto> {
    return this.http.get<AgentConfigDto>(this.apiUrl);
  }

  saveConfig(dto: AgentConfigDto): Observable<AgentConfigDto> {
    return this.http.post<AgentConfigDto>(this.apiUrl, dto);
  }

  deleteConfig(): Observable<void> {
    return this.http.delete<void>(this.apiUrl);
  }

  testConnection(request: AgentConfigTestRequest): Observable<{ status: string; message: string }> {
    return this.http.post<{ status: string; message: string }>(`${this.apiUrl}/test`, request);
  }
}
