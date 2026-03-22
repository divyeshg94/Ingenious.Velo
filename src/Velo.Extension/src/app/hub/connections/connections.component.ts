import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { OrgConnectionService, OrgConnectionDto } from '../../shared/services/org-connection.service';
import { SyncService, SyncResult, WebhookStatus } from '../../shared/services/sync.service';
import { isRunningInADO, getSDK } from '../../shared/services/sdk-initializer.service';

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

  projectSearch = '';
  isLoading = false;
  isSyncing = false;
  isHookLoading = false;
  syncAttempted = false;

  /** True when the API reported it kicked off a background historical sync. */
  autoSyncTriggered = false;

  get filteredProjects(): string[] {
    const q = this.projectSearch.trim().toLowerCase();
    return q ? this.projects.filter(p => p.toLowerCase().includes(q)) : this.projects;
  }

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
    this.errorMessage = '';
    this.orgService.getMyOrganization().subscribe({
      next: (org) => {
        this.currentOrg = org;
        this.isAutoDetected = false;
        this.loadProjects();
      },
      error: () => {
        // When inside ADO and the org isn't registered yet (or API is unreachable),
        // try to self-register using the org name from SDK.getHost().name.
        // This is the "auto-detect" path that fulfils the promise on the empty state.
        if (this.isADO) {
          this.autoConnectFromSDK();
        } else {
          this.isLoading = false;
        }
      }
    });
  }

  /** Auto-register the organisation using the ADO SDK host name when GET /me fails. */
  private autoConnectFromSDK(): void {
    const sdk = getSDK();
    const hostName = sdk?.getHost?.()?.name;

    if (!hostName) {
      // SDK didn't give us a name — fall back to manual entry form
      this.isLoading = false;
      this.errorMessage = 'Could not auto-detect your Azure DevOps organization. Enter your org URL below to connect manually.';
      return;
    }

    const orgUrl = `https://dev.azure.com/${hostName}`;
    this.orgService.connectOrganization(orgUrl).subscribe({
      next: (resp) => {
        this.currentOrg = resp.org;
        this.autoSyncTriggered = resp.autoSyncTriggered;
        this.isAutoDetected = true;
        this.loadProjects();
      },
      error: () => {
        this.isLoading = false;
        this.orgUrl = orgUrl;   // pre-fill the manual form with the detected URL
        this.errorMessage = `Auto-connection to ${orgUrl} failed. Check that the Velo API is reachable and try connecting manually.`;
      }
    });
  }

  loadProjects(): void {
    this.orgService.getAvailableProjects().subscribe({
      next: (projects) => {
        this.projects = projects;
        this.isLoading = false;
        // Auto-load webhook status when a project was already selected (e.g. page reload)
        if (this.selectedProjectId) this.loadHookStatus();
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
        this.syncMessage = `Sync complete — ${result.ingested} pipeline runs ingested.`;
        this.webhookStatus = result.webhook ?? null;
      },
      error: (err) => {
        this.isSyncing = false;
        this.syncAttempted = true;
        this.syncError = err.error?.error || err.message || 'Sync failed — check your permissions (vso.build scope required).';
      }
    });
  }

  loadHookStatus(): void {
    if (!this.selectedProjectId) return;
    this.isHookLoading = true;
    this.webhookError = '';

    this.syncService.getHookStatus(this.selectedProjectId).subscribe({
      next: (status) => {
        this.webhookStatus = status;
        this.syncAttempted = true;
        this.isHookLoading = false;
      },
      error: () => {
        // Status check failed — mark as attempted so the template shows
        // "Check Status Manually" instead of "Click Sync first"
        this.syncAttempted = true;
        this.isHookLoading = false;
      }
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
    this.autoSyncTriggered = false;

    // The auth interceptor already injects X-Ado-Access-Token on every request
    // when running inside Azure DevOps — the API reads it from there.
    this.orgService.connectOrganization(this.orgUrl).subscribe({
      next: (resp) => {
        this.currentOrg = resp.org;
        this.autoSyncTriggered = resp.autoSyncTriggered;
        this.isAutoDetected = false;
        this.orgUrl = '';
        this.loadProjects();
      },
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
    this.autoSyncTriggered = false;

    this.orgService.connectOrganization(this.editOrgUrl).subscribe({
      next: (resp) => {
        this.currentOrg = resp.org;
        this.autoSyncTriggered = resp.autoSyncTriggered;
        this.isAutoDetected = false;
        this.editingUrl = false;
        this.editOrgUrl = '';
        this.loadProjects();
      },
      error: () => { this.updateErrorMessage = 'Failed to update organization URL.'; this.isLoading = false; }
    });
  }
}
