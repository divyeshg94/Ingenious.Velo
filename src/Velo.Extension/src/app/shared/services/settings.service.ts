import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface OrganizationSettingsDto {
  feedbackNotificationEmail?: string;
}

export interface UpdateFeedbackEmailRequest {
  feedbackNotificationEmail?: string;
}

@Injectable({ providedIn: 'root' })
export class SettingsService {
  private apiUrl = `${environment.apiUrl}/api/settings`;

  constructor(private http: HttpClient) {
    console.log('[SettingsService] Initialized with API URL:', this.apiUrl);
  }

  getSettings(): Observable<OrganizationSettingsDto> {
    const url = this.apiUrl;
    console.log('[SettingsService] GET request:', url);
    return this.http.get<OrganizationSettingsDto>(url);
  }

  updateFeedbackEmail(email: string | null): Observable<OrganizationSettingsDto> {
    const url = `${this.apiUrl}/feedback-email`;
    const payload: UpdateFeedbackEmailRequest = { feedbackNotificationEmail: email || undefined };
    console.log('[SettingsService] PUT request:', url);
    return this.http.put<OrganizationSettingsDto>(url, payload);
  }
}
