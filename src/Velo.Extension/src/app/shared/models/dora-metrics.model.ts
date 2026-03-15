export interface DoraMetrics {
  projectId: string;
  orgId: string;
  computedAt: string;
  deploymentFrequency: DeploymentFrequency;
  leadTimeForChanges: LeadTimeForChanges;
  changeFailureRate: ChangeFailureRate;
  meanTimeToRestore: MeanTimeToRestore;
  reworkRate: ReworkRate;
}

export interface DeploymentFrequency {
  deploymentsPerDay: number;
  rating: DoraRating;
}

export interface LeadTimeForChanges {
  averageHours: number;
  rating: DoraRating;
}

export interface ChangeFailureRate {
  percentage: number;
  rating: DoraRating;
}

export interface MeanTimeToRestore {
  averageHours: number;
  rating: DoraRating;
}

export interface ReworkRate {
  percentage: number;
  rating: DoraRating;
}

export type DoraRating = 'Elite' | 'High' | 'Medium' | 'Low';
