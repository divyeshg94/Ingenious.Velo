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

  // ── Build webhook state ──────────────────────────────────────────────────
  webhookStatus: WebhookStatus | null = null;
  webhookError = '';
  isHookLoading = false;

  // ── PR webhook state ──────────────────────────────────────────────────────
  prWebhookStatus: WebhookStatus | null = null;
  prWebhookError = '';
  isPrHookLoading = false;

  // ── Work item webhook state ────────────────────────────────────────────────
  workItemWebhookStatus: WebhookStatus | null = null;
  workItemWebhookError = '';
  isWorkItemHookLoading = false;

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
      this.isLoading = false;
      this.errorMessage = 'Could not auto-detect your Azure DevOps organization. Enter your org URL below to connect manually.';
      return;
    }

    const orgUrl = `https://dev.azure.com/${hostName}`;
    
    // Get the ADO token from the SDK to trigger automatic sync
    const getTokenPromise = sdk?.getAppToken?.();
    if (getTokenPromise) {
      getTokenPromise.then((adoToken: string) => {
        this.orgService.connectOrganization(orgUrl, adoToken).subscribe({
          next: (resp) => {
            this.currentOrg = resp.org;
            this.autoSyncTriggered = resp.autoSyncTriggered;
            this.isAutoDetected = true;
            this.loadProjects();
          },
          error: () => {
            this.isLoading = false;
            this.orgUrl = orgUrl;
            this.errorMessage = `Auto-connection to ${orgUrl} failed. Check that the Velo API is reachable and try connecting manually.`;
          }
        });
      }).catch(() => {
        // If token fetch fails, proceed without token (sync won't auto-trigger)
        this.orgService.connectOrganization(orgUrl).subscribe({
          next: (resp) => {
            this.currentOrg = resp.org;
            this.autoSyncTriggered = resp.autoSyncTriggered;
            this.isAutoDetected = true;
            this.loadProjects();
          },
          error: () => {
            this.isLoading = false;
            this.orgUrl = orgUrl;
            this.errorMessage = `Auto-connection to ${orgUrl} failed. Check that the Velo API is reachable and try connecting manually.`;
          }
        });
      });
    } else {
      // SDK doesn't have getAppToken, proceed without token
      this.orgService.connectOrganization(orgUrl).subscribe({
        next: (resp) => {
          this.currentOrg = resp.org;
          this.autoSyncTriggered = resp.autoSyncTriggered;
          this.isAutoDetected = true;
          this.loadProjects();
        },
        error: () => {
          this.isLoading = false;
          this.orgUrl = orgUrl;
          this.errorMessage = `Auto-connection to ${orgUrl} failed. Check that the Velo API is reachable and try connecting manually.`;
        }
      });
    }
  }

  loadProjects(): void {
    this.orgService.getAvailableProjects().subscribe({
      next: (projects) => {
        this.projects = projects;
        this.isLoading = false;
        // Auto-load webhook status when a project was already selected (e.g. page reload)
        if (this.selectedProjectId) {
          this.loadHookStatus();
          this.loadPrHookStatus();
          this.loadWorkItemHookStatus();
        }
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
    this.webhookStatus         = null;
    this.prWebhookStatus       = null;
    this.workItemWebhookStatus = null;
    this.syncNow();
  }

  syncNow(): void {
    if (!this.selectedProjectId) { this.syncError = 'Select a project first.'; return; }
    this.isSyncing = true;
    this.syncMessage  = '';
    this.syncError    = '';
    this.webhookError = '';
    this.prWebhookError = '';
    this.workItemWebhookError = '';

    this.syncService.syncProject(this.selectedProjectId).subscribe({
      next: (result: SyncResult) => {
        this.isSyncing = false;
        this.syncAttempted         = true;
        this.syncMessage           = `Sync complete — ${result.ingested} pipeline runs ingested.`;
        this.webhookStatus         = result.webhook         ?? null;
        this.prWebhookStatus       = result.prWebhook       ?? null;
        this.workItemWebhookStatus = result.workItemWebhook ?? null;
      },
      error: (err) => {
        this.isSyncing = false;
        this.syncAttempted = true;
        this.syncError = err.error?.error || err.message || 'Sync failed — check your permissions (vso.build scope required).';
      }
    });
  }

  // ── Build webhook actions ─────────────────────────────────────────────────

  loadHookStatus(): void {
    if (!this.selectedProjectId) return;
    this.isHookLoading = true;
    this.webhookError  = '';

    this.syncService.getHookStatus(this.selectedProjectId).subscribe({
      next: (status) => {
        this.webhookStatus = status;
        this.syncAttempted = true;
        this.isHookLoading = false;
      },
      error: () => {
        this.syncAttempted = true;
        this.isHookLoading = false;
      }
    });
  }

  registerHook(): void {
    if (!this.selectedProjectId) return;
    this.isHookLoading = true;
    this.webhookError  = '';

    this.syncService.registerHook(this.selectedProjectId).subscribe({
      next: (status) => { this.webhookStatus = status; this.isHookLoading = false; },
      error: (err) => {
        this.webhookError  = err.error?.registrationError || err.message || 'Failed to register webhook.';
        this.isHookLoading = false;
      }
    });
  }

  removeHook(): void {
    if (!this.webhookStatus?.subscriptionId) return;
    this.isHookLoading = true;
    this.webhookError  = '';

    this.syncService.removeHook(this.webhookStatus.subscriptionId).subscribe({
      next: () => {
        this.webhookStatus = { isRegistered: false, webhookUrl: this.webhookStatus?.webhookUrl, manualSetupUrl: this.webhookStatus?.manualSetupUrl };
        this.isHookLoading = false;
      },
      error: (err) => {
        this.webhookError  = err.message || 'Failed to remove webhook.';
        this.isHookLoading = false;
      }
    });
  }

  // ── PR webhook actions ────────────────────────────────────────────────────

  loadPrHookStatus(): void {
    if (!this.selectedProjectId) return;
    this.isPrHookLoading = true;
    this.prWebhookError  = '';

    this.syncService.getPrHookStatus(this.selectedProjectId).subscribe({
      next: (status) => {
        this.prWebhookStatus = status;
        this.syncAttempted   = true;
        this.isPrHookLoading = false;
      },
      error: () => {
        this.syncAttempted   = true;
        this.isPrHookLoading = false;
      }
    });
  }

  registerPrHook(): void {
    if (!this.selectedProjectId) return;
    this.isPrHookLoading = true;
    this.prWebhookError  = '';

    this.syncService.registerPrHook(this.selectedProjectId).subscribe({
      next: (status) => { this.prWebhookStatus = status; this.isPrHookLoading = false; },
      error: (err) => {
        this.prWebhookError  = err.error?.registrationError || err.message || 'Failed to register PR webhooks.';
        this.isPrHookLoading = false;
      }
    });
  }

  removePrHook(): void {
    if (!this.prWebhookStatus?.subscriptionId) return;
    this.isPrHookLoading = true;
    this.prWebhookError  = '';

    this.syncService.removePrHook(this.prWebhookStatus.subscriptionId).subscribe({
      next: () => {
        this.prWebhookStatus = { isRegistered: false, webhookUrl: this.prWebhookStatus?.webhookUrl, manualSetupUrl: this.prWebhookStatus?.manualSetupUrl };
        this.isPrHookLoading = false;
      },
      error: (err) => {
        this.prWebhookError  = err.message || 'Failed to remove PR webhook.';
        this.isPrHookLoading = false;
      }
    });
  }

  // ── Work item webhook actions ──────────────────────────────────────────────

  loadWorkItemHookStatus(): void {
    if (!this.selectedProjectId) return;
    this.isWorkItemHookLoading = true;
    this.workItemWebhookError  = '';

    this.syncService.getWorkItemHookStatus(this.selectedProjectId).subscribe({
      next: (status) => {
        this.workItemWebhookStatus = status;
        this.syncAttempted         = true;
        this.isWorkItemHookLoading = false;
      },
      error: () => {
        this.syncAttempted         = true;
        this.isWorkItemHookLoading = false;
      }
    });
  }

  registerWorkItemHook(): void {
    if (!this.selectedProjectId) return;
    this.isWorkItemHookLoading = true;
    this.workItemWebhookError  = '';

    this.syncService.registerWorkItemHook(this.selectedProjectId).subscribe({
      next: (status) => { this.workItemWebhookStatus = status; this.isWorkItemHookLoading = false; },
      error: (err) => {
        this.workItemWebhookError  = err.error?.registrationError || err.message || 'Failed to register work item webhook.';
        this.isWorkItemHookLoading = false;
      }
    });
  }

  removeWorkItemHook(): void {
    if (!this.workItemWebhookStatus?.subscriptionId) return;
    this.isWorkItemHookLoading = true;
    this.workItemWebhookError  = '';

    this.syncService.removeWorkItemHook(this.workItemWebhookStatus.subscriptionId).subscribe({
      next: () => {
        this.workItemWebhookStatus = { isRegistered: false, webhookUrl: this.workItemWebhookStatus?.webhookUrl, manualSetupUrl: this.workItemWebhookStatus?.manualSetupUrl };
        this.isWorkItemHookLoading = false;
      },
      error: (err) => {
        this.workItemWebhookError  = err.message || 'Failed to remove work item webhook.';
        this.isWorkItemHookLoading = false;
      }
    });
  }

  // ── Org management ────────────────────────────────────────────────────────

  connectOrg(): void {
    if (!this.orgUrl) return;
    this.isLoading = true;
    this.errorMessage = '';
    this.autoSyncTriggered = false;

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
    this.editingUrl   = !this.editingUrl;
    this.editOrgUrl   = this.currentOrg?.orgUrl || '';
    this.updateErrorMessage = '';
  }

  updateOrgUrl(): void {
    if (!this.editOrgUrl) return;
    this.isLoading = true;
    this.updateErrorMessage = '';
    this.autoSyncTriggered  = false;

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
