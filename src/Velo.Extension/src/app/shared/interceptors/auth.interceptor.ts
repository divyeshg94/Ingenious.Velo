import { Injectable } from '@angular/core';
import { HttpInterceptor, HttpRequest, HttpHandler, HttpEvent, HttpErrorResponse } from '@angular/common/http';
import { Observable, throwError } from 'rxjs';
import { catchError } from 'rxjs/operators';
import * as SDK from 'azure-devops-extension-sdk';

@Injectable()
export class AuthInterceptor implements HttpInterceptor {
  intercept(req: HttpRequest<any>, next: HttpHandler): Observable<HttpEvent<any>> {
    // Get the VSTS token from the Azure DevOps SDK context
    // This token is automatically issued to the extension by ADO and includes the user's org context
    const token = SDK.getAppToken();

    if (token) {
      // Clone the request and add the Authorization header
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
      // Client-side error
      errorMessage = `Error: ${error.error.message}`;
    } else {
      // Server-side error
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
