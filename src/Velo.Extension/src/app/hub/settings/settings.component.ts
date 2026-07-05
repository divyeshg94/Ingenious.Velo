import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule, ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { SettingsService } from '../../shared/services/settings.service';

@Component({
  selector: 'velo-settings',
  standalone: true,
  imports: [CommonModule, FormsModule, ReactiveFormsModule],
  templateUrl: './settings.component.html',
  styleUrls: ['./settings.component.scss'],
})
export class SettingsComponent implements OnInit {
  settingsForm!: FormGroup;
  isLoading = true;
  isSaving = false;
  successMessage: string | null = null;
  errorMessage: string | null = null;

  constructor(
    private fb: FormBuilder,
    private settingsService: SettingsService
  ) {}

  ngOnInit(): void {
    this.initializeForm();
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
        this.errorMessage = 'Failed to load settings. Please try again.';
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

  get emailControl() {
    return this.settingsForm.get('feedbackNotificationEmail');
  }
}
