import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { OrgConnectionService, OrgConnectionDto } from '../../shared/services/org-connection.service';
import { SyncService, SyncResult, WebhookStatus } from '../../shared/services/sync.service';
import { isRunningInADO } from '../../shared/services/sdk-initializer.service';

@Component({
  selector: 'velo-connections',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './connections.component.html',
  styleUrls: ['./connections.component.scss']
})
export class ConnectionsComponent implements OnInit {
  currentOrg: OrgConnectionDto | null = null;
  projects: string[] = [];
  selectedProjectId: string | null = null;
  orgUrl: string = '';
  editingUrl = false;
  editOrgUrl: string = '';
  isAutoDetected = false;
  isADO = true;

  isLoading = false;
  isSyncing = false;
  isHookLoading = false;
  syncAttempted = false;

  errorMessage = '';
  updateErrorMessage = '';
  syncMessage = '';
  syncError = '';

  webhookStatus: WebhookStatus | null = null;
  webhookError = '';

  constructor(
    private orgService: OrgConnectionService,
    private syncService: SyncService
  ) {}

  ngOnInit(): void {
    this.isADO = isRunningInADO();
    this.selectedProjectId = sessionStorage.getItem('selectedProjectId');
    this.loadCurrentOrg();
  }

  loadCurrentOrg(): void {
    this.isLoading = true;
    this.orgService.getMyOrganization().subscribe({
      next: (org) => {
        this.currentOrg = org;
        this.isAutoDetected = false;
        this.loadProjects();
      },
      error: () => { this.isLoading = false; }
    });
  }

  loadProjects(): void {
    this.orgService.getAvailableProjects().subscribe({
      next: (projects) => {
        this.projects = projects;
        this.isLoading = false;
      },
      error: () => {
        this.errorMessage = 'Failed to load projects';
        this.isLoading = false;
      }
    });
  }

  selectProject(projectId: string): void {
    this.selectedProjectId = projectId;
    sessionStorage.setItem('selectedProjectId', projectId);
    this.webhookStatus = null;
    this.syncNow();
  }

  syncNow(): void {
    if (!this.selectedProjectId) { this.syncError = 'Select a project first.'; return; }
    this.isSyncing = true;
    this.syncMessage = '';
    this.syncError = '';
    this.webhookError = '';

    this.syncService.syncProject(this.selectedProjectId).subscribe({
      next: (result: SyncResult) => {
        this.isSyncing = false;
        this.syncAttempted = true;
        this.syncMessage = `✅ Sync complete — ${result.ingested} pipeline runs ingested.`;
        this.webhookStatus = result.webhook ?? null;
      },
      error: (err) => {
        this.isSyncing = false;
        this.syncAttempted = true;
        this.syncError = err.error?.error || err.message || 'Sync failed. Check your permissions (vso.build scope required).';
      }
    });
  }

  loadHookStatus(): void {
    if (!this.selectedProjectId) return;
    this.isHookLoading = true;
    this.webhookError = '';

    this.syncService.getHookStatus(this.selectedProjectId).subscribe({
      next: (status) => { this.webhookStatus = status; this.isHookLoading = false; },
      error: () => { this.isHookLoading = false; }
    });
  }

  registerHook(): void {
    if (!this.selectedProjectId) return;
    this.isHookLoading = true;
    this.webhookError = '';

    this.syncService.registerHook(this.selectedProjectId).subscribe({
      next: (status) => { this.webhookStatus = status; this.isHookLoading = false; },
      error: (err) => {
        this.webhookError = err.error?.registrationError || err.message || 'Failed to register webhook.';
        this.isHookLoading = false;
      }
    });
  }

  removeHook(): void {
    if (!this.webhookStatus?.subscriptionId) return;
    this.isHookLoading = true;
    this.webhookError = '';

    this.syncService.removeHook(this.webhookStatus.subscriptionId).subscribe({
      next: () => {
        this.webhookStatus = { isRegistered: false, webhookUrl: this.webhookStatus?.webhookUrl, manualSetupUrl: this.webhookStatus?.manualSetupUrl };
        this.isHookLoading = false;
      },
      error: (err) => {
        this.webhookError = err.message || 'Failed to remove webhook.';
        this.isHookLoading = false;
      }
    });
  }

  connectOrg(): void {
    if (!this.orgUrl) return;
    this.isLoading = true;
    this.errorMessage = '';
    this.orgService.connectOrganization(this.orgUrl).subscribe({
      next: (org) => { this.currentOrg = org; this.isAutoDetected = false; this.orgUrl = ''; this.loadProjects(); },
      error: () => { this.errorMessage = 'Failed to connect organization.'; this.isLoading = false; }
    });
  }

  toggleEditUrl(): void {
    this.editingUrl = !this.editingUrl;
    this.editOrgUrl = this.currentOrg?.orgUrl || '';
    this.updateErrorMessage = '';
  }

  updateOrgUrl(): void {
    if (!this.editOrgUrl) return;
    this.isLoading = true;
    this.updateErrorMessage = '';
    this.orgService.connectOrganization(this.editOrgUrl).subscribe({
      next: (org) => { this.currentOrg = org; this.isAutoDetected = false; this.editingUrl = false; this.editOrgUrl = ''; this.loadProjects(); },
      error: () => { this.updateErrorMessage = 'Failed to update organization URL.'; this.isLoading = false; }
    });
  }
}
