import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject, interval } from 'rxjs';
import { takeUntil, switchMap, debounceTime } from 'rxjs/operators';
import { UsersService } from '../../shared/services/users.service';

interface User {
  email: string;
  displayName: string | null;
  firstAccessAt: string;
  lastAccessAt: string;
  accessCount: number;
}

interface Statistics {
  totalUsers: number;
  activeUsersLast24Hours: number;
  activeUsersLast7Days: number;
}

@Component({
  selector: 'velo-users-list',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './users-list.component.html',
  styleUrls: ['./users-list.component.scss']
})
export class UsersListComponent implements OnInit, OnDestroy {
  users: User[] = [];
  statistics: Statistics | null = null;
  isLoading = true;
  isLoadingMore = false;
  error: string | null = null;

  skip = 0;
  take = 50;
  hasMore = true;

  private destroy$ = new Subject<void>();
  private refreshTrigger$ = new Subject<void>();

  sortBy: 'lastAccess' | 'accessCount' = 'lastAccess';
  searchQuery = '';

  constructor(private usersService: UsersService) {}

  ngOnInit(): void {
    // Load initial data
    this.loadUsers();
    this.loadStatistics();

    // Auto-refresh every 30 seconds
    interval(30000)
      .pipe(
        switchMap(() => this.refreshTrigger$),
        takeUntil(this.destroy$)
      )
      .subscribe(() => {
        this.loadUsers();
        this.loadStatistics();
      });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  loadUsers(): void {
    if (this.skip === 0) {
      this.isLoading = true;
    } else {
      this.isLoadingMore = true;
    }

    this.usersService.getUsers(this.skip, this.take).subscribe({
      next: (response) => {
        if (this.skip === 0) {
          this.users = response.users;
        } else {
          this.users = [...this.users, ...response.users];
        }
        this.hasMore = response.users.length === this.take;
        this.error = null;
        this.isLoading = false;
        this.isLoadingMore = false;
      },
      error: (err) => {
        console.error('Failed to load users:', err);
        this.error = 'Failed to load users. Please try again.';
        this.isLoading = false;
        this.isLoadingMore = false;
      }
    });
  }

  loadStatistics(): void {
    this.usersService.getStatistics().subscribe({
      next: (stats) => {
        this.statistics = stats;
      },
      error: (err) => {
        console.error('Failed to load statistics:', err);
      }
    });
  }

  loadMore(): void {
    if (this.hasMore && !this.isLoadingMore) {
      this.skip += this.take;
      this.loadUsers();
    }
  }

  resetAndLoad(): void {
    this.skip = 0;
    this.loadUsers();
  }

  getFilteredUsers(): User[] {
    let filtered = this.users;

    if (this.searchQuery.trim()) {
      const query = this.searchQuery.toLowerCase();
      filtered = filtered.filter(u =>
        u.email.toLowerCase().includes(query) ||
        (u.displayName && u.displayName.toLowerCase().includes(query))
      );
    }

    return filtered.sort((a, b) => {
      if (this.sortBy === 'lastAccess') {
        return new Date(b.lastAccessAt).getTime() - new Date(a.lastAccessAt).getTime();
      } else {
        return b.accessCount - a.accessCount;
      }
    });
  }

  formatDate(dateString: string): string {
    const date = new Date(dateString);
    const now = new Date();
    const diffMs = now.getTime() - date.getTime();
    const diffMins = Math.floor(diffMs / 60000);
    const diffHours = Math.floor(diffMs / 3600000);
    const diffDays = Math.floor(diffMs / 86400000);

    if (diffMins < 1) return 'Just now';
    if (diffMins < 60) return `${diffMins} min${diffMins > 1 ? 's' : ''} ago`;
    if (diffHours < 24) return `${diffHours} hour${diffHours > 1 ? 's' : ''} ago`;
    if (diffDays < 7) return `${diffDays} day${diffDays > 1 ? 's' : ''} ago`;

    return date.toLocaleDateString('en-US', {
      month: 'short',
      day: 'numeric',
      year: date.getFullYear() !== now.getFullYear() ? 'numeric' : undefined
    });
  }
}
