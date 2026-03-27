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
  /** Write-only: sent when saving. Never returned by the server. */
  apiKey?: string;
  /** True when an encrypted API key is stored. Returned by server on GET. */
  hasApiKey: boolean;
  updatedAt?: string;
}

export interface AgentConfigTestRequest {
  foundryEndpoint: string;
  agentId: string;
  /** Plaintext key used only during the test — never persisted by this call. */
  apiKey?: string;
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
