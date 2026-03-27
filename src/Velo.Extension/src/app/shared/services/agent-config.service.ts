import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface AgentConfigDto {
  id: string;
  orgId: string;
  foundryEndpoint: string;
  agentId: string;
  displayName?: string;
  isEnabled: boolean;
  /** Write-only: Azure AD Tenant ID for the service principal. Never returned by the server. */
  tenantId?: string;
  /** Write-only: Service principal Client (App) ID. Never returned by the server. */
  clientId?: string;
  /** Write-only: sent when saving. The server encrypts it before storage. Never returned by the server. */
  clientSecret?: string;
  /** True when encrypted service principal credentials are stored. Returned by server on GET. */
  hasServicePrincipal: boolean;
  updatedAt?: string;
}

export interface AgentConfigTestRequest {
  foundryEndpoint: string;
  agentId: string;
  /** Used only during the test — never persisted by this call. */
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
