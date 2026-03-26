import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

/** Nil GUID sent to the API to signal "generate a new ID server-side". */
export const EMPTY_GUID = '00000000-0000-0000-0000-000000000000';

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
    // Ensure id is always a valid GUID string; empty string breaks .NET model binding.
    const payload = { ...dto, id: dto.id || EMPTY_GUID };
    return this.http.post<TeamMappingDto>(this.apiUrl, payload);
  }

  deleteMapping(id: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}`);
  }
}
