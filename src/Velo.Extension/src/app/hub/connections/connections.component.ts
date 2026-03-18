import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { OrgConnectionService, OrgConnectionDto } from '../../shared/services/org-connection.service';
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
  errorMessage = '';
  updateErrorMessage = '';

  constructor(private orgService: OrgConnectionService) {}

  ngOnInit(): void {
    this.isADO = isRunningInADO();
    // Load selected project from sessionStorage
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

    console.log('[Connections] 🔗 Connecting organization:', this.orgUrl);

    this.orgService.connectOrganization(this.orgUrl).subscribe({
      next: (org) => {
        console.log('[Connections] ✅ Organization connected successfully:', org);
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

  toggleEditUrl(): void {
    this.editingUrl = !this.editingUrl;
    this.editOrgUrl = this.currentOrg?.orgUrl || '';
    this.updateErrorMessage = '';
  }

  updateOrgUrl(): void {
    if (!this.editOrgUrl) return;

    this.isLoading = true;
    this.updateErrorMessage = '';

    console.log('[Connections] 🔄 Updating organization URL:', this.editOrgUrl);

    this.orgService.connectOrganization(this.editOrgUrl).subscribe({
      next: (org) => {
        console.log('[Connections] ✅ Organization URL updated successfully:', org);
        this.currentOrg = org;
        this.isAutoDetected = false;
        this.editingUrl = false;
        this.editOrgUrl = '';
        this.loadProjects();
      },
      error: (err) => {
        console.error('[Connections] ❌ Failed to update organization URL:', err);
        this.updateErrorMessage = 'Failed to update organization URL. Please check the URL and try again.';
        this.isLoading = false;
      }
    });
  }

  selectProject(projectId: string): void {
    this.selectedProjectId = projectId;
    sessionStorage.setItem('selectedProjectId', projectId);
    console.log('[Connections] 📁 Project selected and saved:', projectId);
  }
}
