# PR Size Metrics - Quick Start Guide

## For Developers

### Adding PR Insights to a Page

1. **Import the component**:
```typescript
import { PrInsightsComponent } from '../../shared/components/pr-insights/pr-insights.component';

@Component({
  imports: [PrInsightsComponent],
  // ... other config
})
```

2. **Use in template**:
```html
<velo-pr-insights 
  [projectId]="'my-project'" 
  [days]="30">
</velo-pr-insights>
```

3. **With dynamic values**:
```html
<velo-pr-insights 
  [projectId]="selectedProjectId" 
  [days]="selectedDays">
</velo-pr-insights>
```

### Calling PR Metrics API Directly

```typescript
import { PrMetricsService } from '../../shared/services/pr-metrics.service';

constructor(private prMetrics: PrMetricsService) {}

// Get average PR size
this.prMetrics.getAveragePrSize('my-project', 30).subscribe(metrics => {
  console.log(`Avg PR size: ${metrics.averageTotalChanges} lines`);
});

// Get distribution
this.prMetrics.getPrSizeDistribution('my-project', 30).subscribe(dist => {
  console.log(`Small PRs: ${dist.smallPrs}`);
});

// Get top reviewers
this.prMetrics.getTopReviewers('my-project', 10, 30).subscribe(reviewers => {
  reviewers.forEach(r => console.log(`${r.reviewerName}: ${r.prReviewCount} reviews`));
});
```

### Triggered PR Diff Ingestion

```csharp
[Inject]
private readonly IAdoPrDiffIngestService _prDiffService;

// Ingest for single project
var enriched = await _prDiffService.IngestPrDiffsAsync(
    orgId: "myorg",
    projectId: "myproject",
    adoAccessToken: token,
    cancellationToken: ct);

Console.WriteLine($"Enriched {enriched} PRs");
```

### Accessing Metrics

```csharp
[Inject]
private readonly IPrSizeMetricsService _metricsService;

var from = DateTimeOffset.UtcNow.AddDays(-30);
var to = DateTimeOffset.UtcNow;

var metrics = await _metricsService.GetAveragePrSizeAsync(
    orgId: "myorg",
    projectId: "myproject",
    from,
    to,
    cancellationToken);

if (metrics != null)
{
    Console.WriteLine($"Avg PR size: {metrics.AverageTotalChanges} lines");
    Console.WriteLine($"Approval rate: {metrics.ApprovalRate:P}");
    Console.WriteLine($"Avg review time: {metrics.AverageReviewCycleDurationMinutes} min");
}
```

## For End Users

### Viewing PR Insights

1. **Navigate to Team Health Page**
   - Select a project from the left sidebar
   - Look for "Pull Request Insights" section

2. **Key Metrics Card Grid**
   - **Total PRs**: Number of completed pull requests in the period
   - **Avg Files Changed**: Average number of files modified per PR
   - **Avg Total Changes**: Average lines added + deleted
   - **Approval Rate**: % of PRs that received at least one approval (color-coded)
   - **Avg Review Cycle**: How long (in minutes) until first approval
   - **Avg Reviewers**: Average number of reviewers per PR

3. **PR Size Distribution**
   - **Small** (0-100 lines): Quick, focused changes
   - **Medium** (101-500 lines): Standard PR size
   - **Large** (501-1000 lines): Complex changes
   - **Extra Large** (1000+ lines): Major refactors or features

4. **Top Reviewers**
   - Who reviews the most PRs
   - How many they've approved vs rejected
   - Click name to see detailed patterns

### Interpreting Results

**Good signs** 🟢:
- Approval rate > 85%
- Avg review cycle < 4 hours
- Most PRs are small (0-500 lines)
- Even reviewer participation

**Warning signs** 🟡:
- Approval rate dropping below 70%
- Review cycle increasing (bottleneck)
- Many extra-large PRs (hard to review)
- Uneven reviewer workload

**Red flags** 🔴:
- Very low approval rate (< 50%)
- Review cycle > 1 day
- 50%+ of PRs are extra-large
- One person reviewing everything

### Tips for Improvement

- **Reduce PR size**: Aim for 200-400 lines median
- **Distribute reviews**: Rotate reviewers to share workload
- **Set review SLAs**: Target 2-4 hour approval time
- **Use code owners**: Automatically route PRs to domain experts
- **Enable auto-merge**: For simple, approved PRs

## Data Retention

- PR metrics captured automatically via webhooks
- Historical data retained for all time
- Automatic backfill on first organization connection
- Manual re-sync available if needed

## FAQ

**Q: When do metrics update?**
A: Real-time via webhooks. Updates appear within seconds of PR creation/update.

**Q: Why are some numbers missing?**
A: PR diff data may not be available for PRs created before Phase 2 deployment. Run a manual sync to backfill.

**Q: Can I see metrics by team?**
A: Currently by project. Team-level metrics coming in Phase 3.

**Q: How far back do metrics go?**
A: All historical PR data available. Filter by date range in future versions.

**Q: What's included in "changes"?**
A: Lines added + lines deleted (ignoring file renames).

**Q: Why does my approval rate seem low?**
A: Includes all PRs, even those that didn't require approval. Consider project policies.

## Troubleshooting

**No data showing?**
- Ensure PRs have been created after Phase 2 deployment
- Check that webhooks are registered (see Connections page)
- Run manual backfill via admin console

**Numbers seem wrong?**
- Data refreshes within 5 minutes of PR updates
- Try clearing browser cache
- Check that you're in correct project

**Component not rendering?**
- Verify component imported in parent
- Check browser console for errors
- Ensure projectId passed correctly

---

For more details, see:
- **Technical Docs**: `docs/Phase2_PrSizeMetrics.md`
- **Implementation Summary**: `IMPLEMENTATION_SUMMARY.md`
