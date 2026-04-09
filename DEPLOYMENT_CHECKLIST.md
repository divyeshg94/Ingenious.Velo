# Integration Checklist: Average PR Size Metrics (Phase 2)

## Pre-Deployment

- [ ] All code builds successfully without warnings
- [ ] Unit tests written and passing (if applicable)
- [ ] Database migration tested on development database
- [ ] API endpoints tested with Postman/curl
- [ ] Frontend components render without console errors
- [ ] Build artifacts ready for deployment

## Database

- [ ] Migration file reviewed: `20260330_AddPRDiffMetrics.cs`
- [ ] Migration creates 8 new columns
- [ ] Migration creates 2 new indexes
- [ ] Rollback procedure documented
- [ ] Test migration on staging database
- [ ] Backup taken before production migration
- [ ] Migration applied successfully

## Backend (.NET)

**Services Registered**:
- [ ] `IAdoPrDiffIngestService` → `AdoPrDiffIngestService`
- [ ] `IPrSizeMetricsService` → `PrSizeMetricsService`

**Controllers**:
- [ ] `PrMetricsController` registered and routable
- [ ] Three endpoints accessible: `/average-size`, `/distribution`, `/reviewers`

**Webhook Updates**:
- [ ] `WebhookController` captures reviewer names
- [ ] Reviewer data stored as JSON
- [ ] Approval/rejection counts calculated

**Repository Updates**:
- [ ] `MetricsRepository.SavePrEventAsync()` handles all new DTO fields
- [ ] Mapping between DTO and database model complete

## Frontend (Angular)

**Services**:
- [ ] `PrMetricsService` exports all methods
- [ ] DTOs properly typed
- [ ] HTTP client methods work with actual endpoints

**Components**:
- [ ] `PrInsightsComponent` standalone
- [ ] Accepts `@Input` projectId and days
- [ ] Loads and displays all four sections
- [ ] Color coding works correctly
- [ ] Error messages display on failures
- [ ] Loading states functional

**Styling**:
- [ ] SCSS file follows Velo conventions
- [ ] Colors accessible (contrast ratios OK)
- [ ] Responsive layout on mobile
- [ ] Matches existing Velo theme

## Security & Compliance

- [ ] All endpoints require `[Authorize]`
- [ ] OrgId extraction validated
- [ ] Query filters apply org scoping
- [ ] Multi-tenant isolation verified
- [ ] Input validation on all parameters
- [ ] SQL injection prevention confirmed (EF Core)
- [ ] XSS prevention in Angular (no innerHTML)
- [ ] Sensitive data not logged

## Performance

- [ ] API response time < 200ms (tested)
- [ ] Database indexes optimized
- [ ] No N+1 queries identified
- [ ] Query analyzer reviewed for large datasets
- [ ] Caching strategy (if applicable)
- [ ] Load test completed (1000+ concurrent requests)

## Testing

### Unit Tests
- [ ] AdoPrDiffIngestService tests
- [ ] PrSizeMetricsService tests
- [ ] PrMetricsController tests
- [ ] Input validation tests

### Integration Tests
- [ ] Multi-tenancy isolation verified
- [ ] RLS enforcement tested
- [ ] Webhook processing end-to-end
- [ ] API with real database

### Functional Tests
- [ ] Manual test: Average size calculation
- [ ] Manual test: Distribution buckets
- [ ] Manual test: Reviewer list
- [ ] Manual test: Time windows (1, 7, 30, 90 days)
- [ ] Manual test: Empty data handling
- [ ] Manual test: Large dataset (10k+ PRs)

### Browser Tests
- [ ] Chrome (latest)
- [ ] Edge (latest)
- [ ] Firefox (latest)
- [ ] Mobile browser

## Documentation

- [ ] Code comments added for complex logic
- [ ] XML doc comments on public methods
- [ ] `docs/Phase2_PrSizeMetrics.md` complete
- [ ] `docs/PrMetrics_QuickStart.md` complete
- [ ] API endpoints documented
- [ ] DTO properties documented
- [ ] Migration notes included

## Configuration

- [ ] No hardcoded secrets
- [ ] All settings configurable via environment/appsettings
- [ ] CORS settings updated (if needed)
- [ ] Rate limiting considered
- [ ] Logging levels appropriate

## Deployment

### Staging Deployment
- [ ] Deploy database migrations
- [ ] Deploy backend changes
- [ ] Deploy frontend changes
- [ ] Run smoke tests
- [ ] Verify endpoints respond
- [ ] Check error logs

### Production Deployment Steps
1. [ ] Schedule deployment window
2. [ ] Backup production database
3. [ ] Deploy database migrations
4. [ ] Deploy API changes
5. [ ] Deploy frontend changes
6. [ ] Verify all three components operational
7. [ ] Monitor error logs for 1 hour
8. [ ] Test manual ingest
9. [ ] Monitor webhook processing
10. [ ] Rollback plan ready if issues found

## Post-Deployment

- [ ] All endpoints responding with 200 status
- [ ] No errors in application logs
- [ ] Database performance nominal
- [ ] Webhooks processing correctly
- [ ] Frontend component rendering
- [ ] Real data flowing through system
- [ ] User-facing documentation updated
- [ ] Announce feature to users

## Optional: Data Backfill

- [ ] Decision made: backfill immediately or lazy load?
- [ ] If backfilling:
  - [ ] Scheduled during low-traffic period
  - [ ] Estimated time calculated
  - [ ] Monitoring active during backfill
  - [ ] Completion verified
  - [ ] Metrics spot-checked

## Monitoring & Alerts

- [ ] Grafana/App Insights dashboard created
- [ ] Alerts configured for:
  - [ ] API response time > 1000ms
  - [ ] Error rate > 5%
  - [ ] Database query slowdown
  - [ ] Service unavailable
- [ ] Log aggregation working
- [ ] Team trained on new metrics

## Team Communication

- [ ] Feature announced to stakeholders
- [ ] User documentation published
- [ ] Demo recorded (optional)
- [ ] Support team briefed
- [ ] FAQ prepared
- [ ] Release notes prepared
- [ ] Twitter/blog post (if public project)

## Phase 3 Planning

- [ ] Feedback collected from users
- [ ] Enhancement requests documented
- [ ] Performance bottlenecks identified
- [ ] Next phase prioritization started
- [ ] Caching strategy evaluated
- [ ] AI recommendations researched

---

## Sign-Off

- [ ] Development Complete: _________________ (Name/Date)
- [ ] QA Approved: _________________ (Name/Date)
- [ ] Product Owner Approved: _________________ (Name/Date)
- [ ] Security Review Passed: _________________ (Name/Date)
- [ ] Operations/Deployment Ready: _________________ (Name/Date)

---

**Last Updated**: 2024-03-30  
**Version**: Phase 2 - Final  
**Status**: Ready for Deployment
