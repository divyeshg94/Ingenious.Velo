import { HttpErrorResponse } from '@angular/common/http';

const readApiMessage = (errorPayload: unknown): string | null => {
  if (!errorPayload) {
    return null;
  }

  if (typeof errorPayload === 'string') {
    return errorPayload;
  }

  if (typeof errorPayload === 'object') {
    const payload = errorPayload as { error?: unknown; message?: unknown; title?: unknown };

    if (typeof payload.error === 'string' && payload.error.trim()) {
      return payload.error;
    }

    if (typeof payload.message === 'string' && payload.message.trim()) {
      return payload.message;
    }

    if (typeof payload.title === 'string' && payload.title.trim()) {
      return payload.title;
    }
  }

  return null;
};

export const toFriendlyApiError = (error: unknown, fallbackMessage: string): string => {
  if (!(error instanceof HttpErrorResponse)) {
    return error instanceof Error && error.message ? error.message : fallbackMessage;
  }

  const apiMessage = readApiMessage(error.error);

  if (error.status === 0) {
    return 'Cannot reach the Velo API. Open Settings and verify the API Base URL.';
  }

  if (error.status === 404) {
    return 'Resource not found. Verify API Base URL in Settings and reselect your project in Connections.';
  }

  if (error.status === 401 || error.status === 403) {
    return 'Your Azure DevOps session is not authorized. Refresh Azure DevOps and try again.';
  }

  return apiMessage || error.message || fallbackMessage;
};
