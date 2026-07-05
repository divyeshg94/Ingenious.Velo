import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface FeedbackDto {
  id: string;
  feedbackType: string;
  message: string;
  projectId?: string;
  createdAt: string;
  isReviewed: boolean;
}

export interface FeedbackListResponse {
  feedback: FeedbackDto[];
  totalCount: number;
}

export interface FeedbackSubmitRequest {
  feedbackType: string;
  message: string;
  projectId?: string;
}

export interface FeedbackSubmitResponse {
  feedbackId: string;
  message: string;
}

@Injectable({ providedIn: 'root' })
export class FeedbackService {
  private apiUrl = `${environment.apiUrl}/api/feedback`;

  constructor(private http: HttpClient) {
    console.log('[FeedbackService] Initialized with API URL:', this.apiUrl);
  }

  submitFeedback(request: FeedbackSubmitRequest): Observable<FeedbackSubmitResponse> {
    const url = `${this.apiUrl}/submit`;
    console.log('[FeedbackService] Submitting feedback to:', url);
    return this.http.post<FeedbackSubmitResponse>(url, request);
  }

  getFeedback(
    feedbackType?: string,
    skip: number = 0,
    take: number = 50
  ): Observable<FeedbackListResponse> {
    let url = `${this.apiUrl}/list?skip=${skip}&take=${take}`;
    if (feedbackType) {
      url += `&feedbackType=${encodeURIComponent(feedbackType)}`;
    }
    console.log('[FeedbackService] GET request:', url);
    return this.http.get<FeedbackListResponse>(url);
  }
}
