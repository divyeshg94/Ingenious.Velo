import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface User {
  email: string;
  displayName: string | null;
  firstAccessAt: string;
  lastAccessAt: string;
  accessCount: number;
}

export interface UsersListResponse {
  users: User[];
  totalCount: number;
}

export interface UserStatistics {
  totalUsers: number;
  activeUsersLast24Hours: number;
  activeUsersLast7Days: number;
}

@Injectable({
  providedIn: 'root'
})
export class UsersService {
  private readonly apiUrl = `${environment.apiUrl}/users`;

  constructor(private http: HttpClient) {}

  /**
   * Get list of users for the current organization
   * @param skip Number of records to skip (pagination)
   * @param take Number of records to return (pagination)
   * @returns Observable of users list response
   */
  getUsers(skip: number = 0, take: number = 50): Observable<UsersListResponse> {
    let params = new HttpParams()
      .set('skip', skip.toString())
      .set('take', take.toString());

    return this.http.get<UsersListResponse>(`${this.apiUrl}/list`, { params });
  }

  /**
   * Get user statistics for the current organization
   * @returns Observable of user statistics
   */
  getStatistics(): Observable<UserStatistics> {
    return this.http.get<UserStatistics>(`${this.apiUrl}/statistics`);
  }
}
