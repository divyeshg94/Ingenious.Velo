import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule, ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { SettingsService } from '../../shared/services/settings.service';
import { defaultProductionApiBaseUrl } from '../../../environments/api-base-url';
import { environment } from '../../../environments/environment';

@Component({
  selector: 'velo-settings',
  standalone: true,
  imports: [CommonModule, FormsModule, ReactiveFormsModule],
  templateUrl: './settings.component.html',
  styleUrls: ['./settings.component.scss'],
})
export class SettingsComponent implements OnInit {
  private static readonly ApiBaseUrlStorageKey = 'velo-api-base-url';
  private static readonly LegacyApiBaseUrlStorageKey = 'api-base-url';

  settingsForm!: FormGroup;
  isLoading = true;
  isSaving = false;
  successMessage: string | null = null;
  errorMessage: string | null = null;
  apiBaseUrlInput = '';
  apiBaseUrlSuccessMessage: string | null = null;
  apiBaseUrlErrorMessage: string | null = null;
  readonly defaultManagedApiBaseUrl = defaultProductionApiBaseUrl;
  readonly activeApiBaseUrl = environment.apiUrl;
  readonly supportLinks = {
    website: 'https://github.com/divyeshg94/Ingenious.Velo',
    documentation: 'https://github.com/divyeshg94/Ingenious.Velo/tree/master/docs',
    issues: 'https://github.com/divyeshg94/Ingenious.Velo/issues',
  };

  constructor(
    private fb: FormBuilder,
    private settingsService: SettingsService
  ) {}

  ngOnInit(): void {
    this.initializeForm();
    this.loadApiBaseUrlOverride();
    this.loadSettings();
  }

  initializeForm(): void {
    this.settingsForm = this.fb.group({
      feedbackNotificationEmail: ['', [Validators.email]],
    });
  }

  loadSettings(): void {
    this.isLoading = true;
    this.successMessage = null;
    this.errorMessage = null;

    this.settingsService.getSettings().subscribe({
      next: (settings) => {
        this.settingsForm.patchValue({
          feedbackNotificationEmail: settings.feedbackNotificationEmail || '',
        });
        this.isLoading = false;
      },
      error: (err) => {
        this.errorMessage = 'Failed to load settings from the API. If this started after an update, set API Base URL below and reload.';
        this.isLoading = false;
      },
    });
  }

  onSave(): void {
    if (!this.settingsForm.valid) {
      this.errorMessage = 'Please enter a valid email address.';
      return;
    }

    this.isSaving = true;
    this.successMessage = null;
    this.errorMessage = null;

    const email = this.settingsForm.get('feedbackNotificationEmail')?.value || null;

    this.settingsService.updateFeedbackEmail(email).subscribe({
      next: () => {
        this.successMessage = 'Settings saved successfully!';
        this.isSaving = false;
        setTimeout(() => {
          this.successMessage = null;
        }, 3000);
      },
      error: (err) => {
        this.errorMessage = err.error?.error || 'Failed to save settings. Please try again.';
        this.isSaving = false;
      },
    });
  }

  onClear(): void {
    this.settingsForm.reset();
    this.onSave();
  }

  saveApiBaseUrl(): void {
    const input = this.apiBaseUrlInput.trim();
    this.apiBaseUrlSuccessMessage = null;
    this.apiBaseUrlErrorMessage = null;

    if (!input) {
      this.clearApiBaseUrlOverride();
      this.apiBaseUrlSuccessMessage = 'Using managed Velo API endpoint. Reloading...';
      setTimeout(() => window.location.reload(), 500);
      return;
    }

    const normalizedUrl = this.tryNormalizeHttpUrl(input);
    if (!normalizedUrl) {
      this.apiBaseUrlErrorMessage = 'Please enter a valid absolute http(s) URL (example: https://api.your-company.com).';
      return;
    }

    localStorage.setItem(SettingsComponent.ApiBaseUrlStorageKey, normalizedUrl);
    localStorage.setItem(SettingsComponent.LegacyApiBaseUrlStorageKey, normalizedUrl);
    this.apiBaseUrlSuccessMessage = 'API base URL saved. Reloading...';
    setTimeout(() => window.location.reload(), 500);
  }

  resetApiBaseUrl(): void {
    this.apiBaseUrlSuccessMessage = null;
    this.apiBaseUrlErrorMessage = null;
    this.clearApiBaseUrlOverride();
    this.apiBaseUrlInput = '';
    this.apiBaseUrlSuccessMessage = 'API base URL reset to managed endpoint. Reloading...';
    setTimeout(() => window.location.reload(), 500);
  }

  private loadApiBaseUrlOverride(): void {
    const override = localStorage.getItem(SettingsComponent.ApiBaseUrlStorageKey)?.trim()
      || localStorage.getItem(SettingsComponent.LegacyApiBaseUrlStorageKey)?.trim()
      || '';

    this.apiBaseUrlInput = override;
  }

  private clearApiBaseUrlOverride(): void {
    localStorage.removeItem(SettingsComponent.ApiBaseUrlStorageKey);
    localStorage.removeItem(SettingsComponent.LegacyApiBaseUrlStorageKey);
  }

  private tryNormalizeHttpUrl(value: string): string | null {
    try {
      const url = new URL(value);
      if (url.protocol !== 'http:' && url.protocol !== 'https:') {
        return null;
      }

      return url.toString().replace(/\/+$/, '');
    } catch {
      return null;
    }
  }

  get emailControl() {
    return this.settingsForm.get('feedbackNotificationEmail');
  }
}
