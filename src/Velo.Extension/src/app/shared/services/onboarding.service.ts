import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface OnboardingProgressDto {
  hasSyncedPipeline: boolean;
  hasViewedDoraMetric: boolean;
  hasSharedDashboard: boolean;
  registeredAt: string | null;
  isComplete: boolean;
}

@Injectable({ providedIn: 'root' })
export class OnboardingService {
  private apiUrl = `${environment.apiUrl}/api/orgs`;

  constructor(private http: HttpClient) {}

  getProgress(): Observable<OnboardingProgressDto> {
    return this.http.get<OnboardingProgressDto>(`${this.apiUrl}/onboarding`);
  }
}
