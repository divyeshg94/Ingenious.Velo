import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { DoraMetrics } from '../models/dora-metrics.model';
import { environment } from '../../../environments/environment';

@Injectable({ providedIn: 'root' })
export class DoraService {
  private readonly baseUrl = `${environment.apiUrl}/dora`;

  constructor(private http: HttpClient) {}

  getMetrics(projectId: string, days = 30): Observable<DoraMetrics> {
    return this.http.get<DoraMetrics>(`${this.baseUrl}/metrics`, {
      params: { projectId, days },
    });
  }

  getMetricsHistory(projectId: string, days = 90): Observable<DoraMetrics[]> {
    return this.http.get<DoraMetrics[]>(`${this.baseUrl}/history`, {
      params: { projectId, days },
    });
  }
}
