import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { OrgConnectionService, OrgConnectionDto } from '../../shared/services/org-connection.service';
import { SyncService, SyncResult } from '../../shared/services/sync.service';
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
  errorMessage = '';
  updateErrorMessage = '';
  syncMessage = '';
  syncError = '';

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
        console.log('[Connections] ✅ Organization loaded:', org);
        this.currentOrg = org;
        this.isAutoDetected = false;
        this.loadProjects();
      },
      error: (err) => {
        console.log('[Connections] ℹ️ No organization connected yet');
        this.isLoading = false;
      }
    });
  }

  loadProjects(): void {
    this.orgService.getAvailableProjects().subscribe({
      next: (projects) => {
        console.log('[Connections] ✅ Projects loaded:', projects.length);
        this.projects = projects;
        this.isLoading = false;
      },
      error: (err) => {
        console.error('[Connections] ❌ Failed to load projects:', err);
        this.errorMessage = 'Failed to load projects';
        this.isLoading = false;
      }
    });
  }

  connectOrg(): void {
    if (!this.orgUrl) return;
    this.isLoading = true;
    this.errorMessage = '';
    this.orgService.connectOrganization(this.orgUrl).subscribe({
      next: (org) => {
        this.currentOrg = org;
        this.isAutoDetected = false;
        this.orgUrl = '';
        this.loadProjects();
      },
      error: (err) => {
        console.error('[Connections] ❌ Failed to connect organization:', err);
        this.errorMessage = 'Failed to connect organization. Please check the URL and try again.';
        this.isLoading = false;
      }
    });
  }

  selectProject(projectId: string): void {
    this.selectedProjectId = projectId;
    sessionStorage.setItem('selectedProjectId', projectId);
    console.log('[Connections] 📁 Project selected and saved:', projectId);
    // Auto-sync when a project is first selected
    this.syncNow();
  }

  syncNow(): void {
    if (!this.selectedProjectId) {
      this.syncError = 'Select a project first.';
      return;
    }

    this.isSyncing = true;
    this.syncMessage = '';
    this.syncError = '';

    console.log('[Connections] 🔄 Syncing pipeline data for:', this.selectedProjectId);

    this.syncService.syncProject(this.selectedProjectId).subscribe({
      next: (result: SyncResult) => {
        console.log('[Connections] ✅ Sync complete:', result);
        this.isSyncing = false;
        this.syncMessage = `✅ Sync complete — ${result.ingested} pipeline runs ingested. Dashboard is ready.`;
      },
      error: (err) => {
        console.error('[Connections] ❌ Sync failed:', err);
        this.isSyncing = false;
        this.syncError = err.message || 'Sync failed. Check your permissions (vso.build scope required).';
      }
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
      next: (org) => {
        this.currentOrg = org;
        this.isAutoDetected = false;
        this.editingUrl = false;
        this.editOrgUrl = '';
        this.loadProjects();
      },
      error: (err) => {
        this.updateErrorMessage = 'Failed to update organization URL.';
        this.isLoading = false;
      }
    });
  }
}
