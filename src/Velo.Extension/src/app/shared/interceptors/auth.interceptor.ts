import { Injectable } from '@angular/core';
import { HttpInterceptor, HttpRequest, HttpHandler, HttpEvent, HttpErrorResponse } from '@angular/common/http';
import { Observable, throwError, from } from 'rxjs';
import { catchError, switchMap } from 'rxjs/operators';
import { getSDK, isRunningInADO } from '../services/sdk-initializer.service';
import { DEV_MOCK_JWT } from '../services/mock-sdk.service';

@Injectable()
export class AuthInterceptor implements HttpInterceptor {
  intercept(req: HttpRequest<any>, next: HttpHandler): Observable<HttpEvent<any>> {
    if (isRunningInADO()) {
      // ADO SDK v4: getAppToken() returns the extension token (for our API auth)
      //             getAccessToken() returns an ADO-scoped token (for calling ADO REST APIs)
      const SDK = getSDK();
      return from(Promise.all([
        Promise.resolve(SDK.getAppToken?.()),
        Promise.resolve(SDK.getAccessToken?.())
      ])).pipe(
        switchMap(([appToken, accessToken]: [string | undefined, string | undefined]) => {
          const orgName = SDK.getHost?.()?.name || '';
          const headers: Record<string, string> = { 'Content-Type': 'application/json' };
          if (appToken) headers['Authorization'] = `Bearer ${appToken}`;
          if (orgName) headers['X-Azure-DevOps-OrgId'] = orgName;
          if (accessToken) headers['X-Ado-Access-Token'] = accessToken;
          return next.handle(req.clone({ setHeaders: headers }));
        }),
        catchError((error: HttpErrorResponse) => this.handleError(error))
      );
    }

    // Local development — synchronous mock token
    const token = localStorage.getItem('mock-token') || DEV_MOCK_JWT;
    const orgId = localStorage.getItem('mock-org-id') || 'local-org-dev';
    console.log('[Auth Interceptor] Using local mock token');
    const cloned = req.clone({
      setHeaders: {
        Authorization: `Bearer ${token}`,
        'Content-Type': 'application/json',
        'X-Azure-DevOps-OrgId': orgId
      }
    });
    return next.handle(cloned).pipe(
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
