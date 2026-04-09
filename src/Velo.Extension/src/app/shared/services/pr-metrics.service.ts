import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

/**
 * Average PR size metrics for a project over a period.
 */
export interface PrSizeMetricsDto {
  orgId: string;
  projectId: string;
  periodStart: string;
  periodEnd: string;
  totalPrCount: number;
  averageFilesChanged: number;
  averageLinesAdded: number;
  averageLinesDeleted: number;
  averageTotalChanges: number;
  averageReviewCycleDurationMinutes: number;
  approvalRate: number;
  averageReviewerCount: number;
  computedAt: string;
}

/**
 * PR size distribution across buckets.
 */
export interface PrSizeDistributionDto {
  smallPrs: number;      // 0-100 lines
  mediumPrs: number;     // 101-500 lines
  largePrs: number;      // 501-1000 lines
  extraLargePrs: number; // 1000+ lines
}

/**
 * Reviewer insights and participation metrics.
 */
export interface ReviewerInsightsDto {
  reviewerName: string;
  prReviewCount: number;
  approvalCount: number;
  rejectionCount: number;
}

@Injectable({ providedIn: 'root' })
export class PrMetricsService {
  private apiUrl = `${environment.apiUrl}/api/pr-metrics`;

  constructor(private http: HttpClient) {}

  /**
   * Get average PR size metrics for a project.
   * @param projectId The project identifier
   * @param days Number of days to look back (default: 30)
   */
  getAveragePrSize(projectId: string, days: number = 30): Observable<PrSizeMetricsDto> {
    return this.http.get<PrSizeMetricsDto>(`${this.apiUrl}/average-size`, {
      params: { projectId, days: days.toString() }
    });
  }

  /**
   * Get PR size distribution for a project.
   * @param projectId The project identifier
   * @param days Number of days to look back (default: 30)
   */
  getPrSizeDistribution(projectId: string, days: number = 30): Observable<PrSizeDistributionDto> {
    return this.http.get<PrSizeDistributionDto>(`${this.apiUrl}/distribution`, {
      params: { projectId, days: days.toString() }
    });
  }

  /**
   * Get top reviewers by participation.
   * @param projectId The project identifier
   * @param topCount Number of top reviewers to return (default: 10)
   * @param days Number of days to look back (default: 30)
   */
  getTopReviewers(projectId: string, topCount: number = 10, days: number = 30): Observable<ReviewerInsightsDto[]> {
    return this.http.get<ReviewerInsightsDto[]>(`${this.apiUrl}/reviewers`, {
      params: { projectId, topCount: topCount.toString(), days: days.toString() }
    });
  }
}
