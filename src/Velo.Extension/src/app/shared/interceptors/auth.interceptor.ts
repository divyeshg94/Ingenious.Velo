import { Injectable } from '@angular/core';
import { HttpInterceptor, HttpRequest, HttpHandler, HttpEvent, HttpErrorResponse } from '@angular/common/http';
import { Observable, throwError } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { getSDK, isRunningInADO } from '../services/sdk-initializer.service';

@Injectable()
export class AuthInterceptor implements HttpInterceptor {
  intercept(req: HttpRequest<any>, next: HttpHandler): Observable<HttpEvent<any>> {
    let token: string | null = null;

    if (isRunningInADO()) {
      // Get token from ADO SDK
      const SDK = getSDK();
      token = SDK.getAppToken?.();
    } else {
      // Local development - get token from localStorage or use mock
      token = localStorage.getItem('mock-token') || 'mock-token-for-local-dev';
      console.log('[Auth Interceptor] Using local mock token');
    }

    if (token) {
      req = req.clone({
        setHeaders: {
          Authorization: `Bearer ${token}`,
          'Content-Type': 'application/json'
        }
      });
    }

    return next.handle(req).pipe(
      catchError((error: HttpErrorResponse) => this.handleError(error))
    );
  }

  private handleError(error: HttpErrorResponse): Observable<never> {
    let errorMessage = 'An error occurred';

    if (error.error instanceof ErrorEvent) {
      errorMessage = `Error: ${error.error.message}`;
    } else {
      if (error.status === 401) {
        errorMessage = 'Unauthorized - Please reauthenticate';
      } else if (error.status === 403) {
        errorMessage = 'Forbidden - You do not have permission to access this resource';
      } else if (error.status === 404) {
        errorMessage = 'Resource not found';
      } else if (error.status === 429) {
        errorMessage = 'Too many requests - Please try again later';
      } else if (error.status >= 500) {
        errorMessage = 'Server error - Please try again later';
      } else {
        errorMessage = error.error?.error || `Server returned code ${error.status}`;
      }
    }

    console.error('HTTP Error:', errorMessage, error);
    return throwError(() => new Error(errorMessage));
  }
}
