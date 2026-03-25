import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface TeamMappingDto {
  id: string;
  orgId: string;
  projectId: string;
  repositoryName: string;
  teamName: string;
}

@Injectable({ providedIn: 'root' })
export class TeamMappingService {
  private readonly apiUrl = `${environment.apiUrl}/api/teammappings`;

  constructor(private http: HttpClient) {}

  getMappings(projectId: string): Observable<TeamMappingDto[]> {
    return this.http.get<TeamMappingDto[]>(`${this.apiUrl}?projectId=${encodeURIComponent(projectId)}`);
  }

  getRepositories(projectId: string): Observable<string[]> {
    return this.http.get<string[]>(`${this.apiUrl}/repositories?projectId=${encodeURIComponent(projectId)}`);
  }

  saveMapping(dto: TeamMappingDto): Observable<TeamMappingDto> {
    return this.http.post<TeamMappingDto>(this.apiUrl, dto);
  }

  deleteMapping(id: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}`);
  }
}
