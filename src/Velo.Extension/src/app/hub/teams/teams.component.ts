import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TeamMappingService, TeamMappingDto } from '../../shared/services/team-mapping.service';
import { isRunningInADO, getSDK } from '../../shared/services/sdk-initializer.service';

interface MappingRow {
  id: string;
  repositoryName: string;
  teamName: string;
  /** true while being edited inline */
  editing: boolean;
  /** draft value while editing */
  draft: string;
  saving: boolean;
  deleting: boolean;
}

@Component({
  selector: 'velo-teams',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './teams.component.html',
  styleUrls: ['./teams.component.scss'],
})
export class TeamsComponent implements OnInit {
  selectedProjectId: string | null = null;

  /** All repo names discovered from PipelineRuns for the selected project */
  repositories: string[] = [];
  /** Rows shown in the mapping table */
  rows: MappingRow[] = [];

  /** Add-new form state */
  newRepo = '';
  newTeam = '';
  adding = false;

  isLoading = false;
  errorMessage = '';
  successMessage = '';

  constructor(private svc: TeamMappingService) {}

  ngOnInit(): void {
    this.selectedProjectId = sessionStorage.getItem('selectedProjectId');
    if (!this.selectedProjectId && isRunningInADO()) {
      const ctx = getSDK()?.getWebContext?.();
      if (ctx?.project?.name) {
        this.selectedProjectId = ctx.project.name;
        sessionStorage.setItem('selectedProjectId', this.selectedProjectId!);
      }
    }
    if (this.selectedProjectId) this.load();
  }

  // ── Data loading ────────────────────────────────────────────────────

  load(): void {
    if (!this.selectedProjectId) return;
    this.isLoading = true;
    this.errorMessage = '';

    this.svc.getRepositories(this.selectedProjectId).subscribe({
      next: repos => {
        this.repositories = repos;
        this.loadMappings();
      },
      error: () => {
        this.errorMessage = 'Could not load repositories. Run a sync first.';
        this.isLoading = false;
      }
    });
  }

  private loadMappings(): void {
    if (!this.selectedProjectId) return;
    this.svc.getMappings(this.selectedProjectId).subscribe({
      next: mappings => {
        this.rows = mappings.map(m => ({
          id: m.id,
          repositoryName: m.repositoryName,
          teamName: m.teamName,
          editing: false,
          draft: m.teamName,
          saving: false,
          deleting: false,
        }));
        this.isLoading = false;
      },
      error: () => {
        this.errorMessage = 'Could not load team mappings.';
        this.isLoading = false;
      }
    });
  }

  // ── Computed helpers ────────────────────────────────────────────────

  /** Repos that don't yet have a mapping */
  get unmappedRepos(): string[] {
    const mapped = new Set(this.rows.map(r => r.repositoryName));
    return this.repositories.filter(r => !mapped.has(r));
  }

  // ── Inline edit ─────────────────────────────────────────────────────

  startEdit(row: MappingRow): void {
    row.draft = row.teamName;
    row.editing = true;
  }

  cancelEdit(row: MappingRow): void {
    row.editing = false;
  }

  saveEdit(row: MappingRow): void {
    const name = row.draft.trim();
    if (!name) return;
    row.saving = true;
    this.errorMessage = '';
    const dto: TeamMappingDto = {
      id: row.id,
      orgId: '',
      projectId: this.selectedProjectId!,
      repositoryName: row.repositoryName,
      teamName: name,
    };
    this.svc.saveMapping(dto).subscribe({
      next: saved => {
        row.teamName = saved.teamName;
        row.id = saved.id;
        row.editing = false;
        row.saving = false;
        this.flash('Mapping updated.');
      },
      error: () => {
        row.saving = false;
        this.errorMessage = 'Failed to save mapping.';
      }
    });
  }

  // ── Delete ──────────────────────────────────────────────────────────

  deleteRow(row: MappingRow): void {
    if (!confirm(`Remove "${row.repositoryName}" → "${row.teamName}"?`)) return;
    row.deleting = true;
    this.svc.deleteMapping(row.id).subscribe({
      next: () => {
        this.rows = this.rows.filter(r => r !== row);
        this.flash('Mapping removed.');
      },
      error: () => {
        row.deleting = false;
        this.errorMessage = 'Failed to delete mapping.';
      }
    });
  }

  // ── Add new ─────────────────────────────────────────────────────────

  addMapping(): void {
    const repo = this.newRepo.trim();
    const team = this.newTeam.trim();
    if (!repo || !team || !this.selectedProjectId) return;
    this.adding = true;
    this.errorMessage = '';
    const dto: TeamMappingDto = {
      id: '',
      orgId: '',
      projectId: this.selectedProjectId,
      repositoryName: repo,
      teamName: team,
    };
    this.svc.saveMapping(dto).subscribe({
      next: saved => {
        this.rows.push({
          id: saved.id,
          repositoryName: saved.repositoryName,
          teamName: saved.teamName,
          editing: false,
          draft: saved.teamName,
          saving: false,
          deleting: false,
        });
        this.newRepo = '';
        this.newTeam = '';
        this.adding = false;
        this.flash('Mapping added.');
      },
      error: () => {
        this.adding = false;
        this.errorMessage = 'Failed to add mapping.';
      }
    });
  }

  // ── Helpers ─────────────────────────────────────────────────────────

  private flash(msg: string): void {
    this.successMessage = msg;
    setTimeout(() => (this.successMessage = ''), 3000);
  }
}
