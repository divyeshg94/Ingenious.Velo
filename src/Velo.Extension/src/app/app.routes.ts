import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    redirectTo: 'dashboard',
    pathMatch: 'full',
  },
  {
    path: 'dashboard',
    loadComponent: () =>
      import('./hub/dashboard/dashboard.component').then((m) => m.DashboardComponent),
  },
  {
    path: 'dora',
    loadComponent: () =>
      import('./hub/dora/dora.component').then((m) => m.DoraComponent),
  },
  {
    path: 'health',
    loadComponent: () =>
      import('./hub/health/health.component').then((m) => m.HealthComponent),
  },
  {
    path: 'agent',
    loadComponent: () =>
      import('./hub/agent/agent.component').then((m) => m.AgentComponent),
  },
  {
    path: 'pipelines',
    loadComponent: () =>
      import('./hub/pipelines/pipelines.component').then((m) => m.PipelinesComponent),
  },
  {
    path: 'connections',
    loadComponent: () =>
      import('./hub/connections/connections.component').then((m) => m.ConnectionsComponent),
  },
  {
    path: '**',
    redirectTo: 'dashboard',
  },
];
