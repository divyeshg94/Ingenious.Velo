# Analysis Output Templates

Use these structured templates when returning analysis results to ensure consistent, parseable output.

## Pipeline Bottleneck Analysis

```
## Pipeline Bottleneck Report: {pipeline_name}

**Analyzed**: {run_count} runs over {days} days

### Slowest Stages
| Stage | Avg Duration | P95 Duration | Failure Rate |
|-------|-------------|--------------|-------------|
| {stage} | {avg} | {p95} | {rate}% |

### Root Causes
1. {cause_1}
2. {cause_2}

### Recommendations
- **{priority}**: {recommendation}
  ```yaml
  # Before
  {before_yaml}
  # After
  {after_yaml}
  ```
```

## DORA Metrics Interpretation

```
## DORA Performance Summary: {project_name}

| Metric | Value | Rating | Trend |
|--------|-------|--------|-------|
| Deployment Frequency | {value}/day | {rating} | {trend} |
| Lead Time for Changes | {value}h | {rating} | {trend} |
| Change Failure Rate | {value}% | {rating} | {trend} |
| Mean Time to Restore | {value}h | {rating} | {trend} |
| Rework Rate | {value}% | {rating} | {trend} |

### Key Insights
{insights}

### Top Priority Action
{action}
```
