import { Component, EventEmitter, OnInit, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule, ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { FeedbackService, FeedbackSubmitRequest } from '../../shared/services/feedback.service';

@Component({
  selector: 'velo-feedback-modal',
  standalone: true,
  imports: [CommonModule, FormsModule, ReactiveFormsModule],
  templateUrl: './feedback-modal.component.html',
  styleUrls: ['./feedback-modal.component.scss'],
})
export class FeedbackModalComponent implements OnInit {
  @Output() closed = new EventEmitter<void>();

  feedbackForm!: FormGroup;
  isSubmitting = false;
  submitMessage: string | null = null;
  submitError: string | null = null;

  feedbackTypes = [
    { value: 'Bug', label: 'Bug Report' },
    { value: 'FeatureRequest', label: 'Feature Request' },
    { value: 'MetricConcern', label: 'Metric Concern' },
    { value: 'PerformanceIssue', label: 'Performance Issue' },
  ];

  constructor(
    private fb: FormBuilder,
    private feedbackService: FeedbackService
  ) {}

  ngOnInit(): void {
    this.feedbackForm = this.fb.group({
      feedbackType: ['Bug', [Validators.required]],
      message: ['', [Validators.required, Validators.minLength(10), Validators.maxLength(2000)]],
      projectId: [''],
    });
  }

  onSubmit(): void {
    if (!this.feedbackForm.valid) {
      this.submitError = 'Please fill in all required fields.';
      return;
    }

    this.isSubmitting = true;
    this.submitError = null;
    this.submitMessage = null;

    const request: FeedbackSubmitRequest = this.feedbackForm.value;

    this.feedbackService.submitFeedback(request).subscribe({
      next: (response) => {
        this.submitMessage = response.message;
        this.feedbackForm.reset({ feedbackType: 'Bug' });
        this.isSubmitting = false;
        // Close modal after a short delay
        setTimeout(() => this.closeModal(), 2000);
      },
      error: (err) => {
        this.submitError = err.error?.error || 'Failed to submit feedback. Please try again.';
        this.isSubmitting = false;
      },
    });
  }

  closeModal(): void {
    this.closed.emit();
  }

  get messageControl() {
    return this.feedbackForm.get('message');
  }

  get remainingChars(): number {
    const message = this.feedbackForm.get('message')?.value || '';
    return 2000 - message.length;
  }
}
