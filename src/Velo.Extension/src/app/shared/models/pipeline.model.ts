export interface PipelineRun {
  id: string;
  pipelineId: number;
  pipelineName: string;
  runNumber: string;
  status: 'succeeded' | 'failed' | 'canceled' | 'inProgress';
  result: 'succeeded' | 'failed' | 'canceled' | 'partiallySucceeded' | null;
  startTime: string;
  finishTime: string | null;
  durationMs: number | null;
  isDeployment: boolean;
  stageName: string | null;
  triggeredBy: string;
}
