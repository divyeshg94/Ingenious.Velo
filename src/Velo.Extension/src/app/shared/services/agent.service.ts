import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface ChatMessage {
  role: 'user' | 'assistant';
  content: string;
}

export interface AgentChatRequest {
  projectId: string;
  message: string;
  history: ChatMessage[];
}

export interface AgentChatResponse {
  message: ChatMessage;
  citations: string[];
}

@Injectable({ providedIn: 'root' })
export class AgentService {
  private readonly apiUrl = `${environment.apiUrl}/api/agent`;

  constructor(private http: HttpClient) {}

  chat(request: AgentChatRequest): Observable<AgentChatResponse> {
    return this.http.post<AgentChatResponse>(`${this.apiUrl}/chat`, request);
  }
}
