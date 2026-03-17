import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface AgentMessage {
  role: 'user' | 'assistant';
  content: string;
  timestamp: string;
}

export interface AgentChatRequest {
  projectId: string;
  message: string;
  history: AgentMessage[];
}

export interface AgentChatResponse {
  message: AgentMessage;
  citations: string[];
}

@Injectable({ providedIn: 'root' })
export class AgentService {
  private readonly baseUrl = `${environment.apiUrl}/agent`;

  constructor(private http: HttpClient) {}

  chat(request: AgentChatRequest): Observable<AgentChatResponse> {
    return this.http.post<AgentChatResponse>(`${this.baseUrl}/chat`, request);
  }
}
