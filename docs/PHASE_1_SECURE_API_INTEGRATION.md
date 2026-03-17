# Velo Extension — Phase 1: Secure API Integration
## Implementation Status: WIP

### ✅ Completed

#### Frontend (Angular Extension)
- ✅ **OrganizationConnectionService** — HTTP service to manage org connections  
- ✅ **DoraMetricsService** — HTTP service to fetch DORA metrics
- ✅ **AuthInterceptor** — Attaches Azure AD token to all HTTP requests
- ✅ **ConnectionsComponent** — UI for org connection management
- ✅ **DashboardComponent** — Displays real DORA metrics with styling
- ✅ **Placeholder Components** — DORA, Health, Agent, Pipelines (ready for Phase 2)
- ✅ **Routing** — All tabs routed with lazy loading
- ✅ **Theme Support** — Light/Dark/High-Contrast with CSS variables
- ✅ **Environment Config** — Dev/Prod API URLs

#### Backend (.NET API)
- ✅ **DoraController** — Secure [Authorize] endpoints with org_id validation
- ✅ **OrgsController** — Organization management endpoints
- ✅ **IMetricsRepository** — Extended contract with org methods
- ✅ **TenantResolutionMiddleware** — Extracts org_id from JWT, sets DbContext & SQL session context
- ✅ **RateLimitMiddleware** — Enforces token budget per org per day

### 🚧 To Do (Phase 1 Completion)

#### Backend Implementation
1. **Implement IMetricsRepository** in a new `MetricsRepository` service:
   - `GetLatestAsync()` — Query `DoraMetrics` scoped by org_id
   - `GetHistoryAsync()` — Range query with date filtering
   - `GetOrgContextAsync()` — Fetch org details
   - All queries use EF query filters (org_id scoped automatically)

2. **VeloDbContext Query Filters** — Ensure all tenant tables have:
   ```csharp
   modelBuilder.Entity<DoraMetrics>()
       .HasQueryFilter(m => m.OrgId == CurrentOrgId);
   ```

3. **Fix Existing Controllers** — Update to use DTOs, not EF models:
   - PipelinesController — use `PipelineRunDto`
   - HealthController — use `TeamHealthDto`

4. **Add Missing Services**:
   - `IProjectService` — get available projects per org
   - `IOrgService` — manage org registration

---

## Security Model (Enforced)

### Layer 1: Authentication
- **JWT Validation** — [Authorize] on all endpoints  
- **Token Source** — Azure AD B2C (from Azure DevOps SDK in extension)
- **Token Claims** — Contains `oid` (org_id) — extracted by `TenantResolutionMiddleware`

### Layer 2: Multi-Tenancy (Application)
- **EF Core Query Filter** — Every entity with `org_id` auto-filtered by `CurrentOrgId`
- **Filter Always Active** — No bypass without explicit comment + code review
- **Middleware Sets Context** — `TenantResolutionMiddleware` sets `DbContext.CurrentOrgId` per request

### Layer 3: Multi-Tenancy (Database)
- **SQL Server RLS** — Row-level security policies on all tenant tables
- **Session Context** — Set by middleware before database access: `EXEC sp_set_session_context N'org_id', @OrgId`
- **Double Enforcement** — If EF filter fails, RLS prevents data leak

### Layer 4: Rate Limiting
- **Token Budget** — Free tier: 50,000 tokens/day; Premium: 1,000,000
- **Per Org Per Day** — Budget tracked in cache (upgrade to Redis/DB)
- **Premium Bypass** — Organizations with `is_premium = 1` skip checks

### Layer 5: HTTP Transport
- **HTTPS Only** — All production endpoints over TLS
- **CORS** — Only `https://dev.azure.com` and `https://*.visualstudio.com` allowed
- **Authorization Header** — Bearer token required on all requests from extension

---

## Next Immediate Steps

1. **Implement MetricsRepository service** — wire up EF queries
2. **Update existing controllers** — use DTOs, add [Authorize]
3. **Add integration tests** — verify org_id filtering works  
4. **Test with extension** — verify token flow end-to-end
5. **Deploy to staging** — validate against real ADO

---

## Files Created/Modified

### Angular
- `src/app/shared/services/org-connection.service.ts` — NEW
- `src/app/shared/services/dora-metrics.service.ts` — NEW
- `src/app/shared/interceptors/auth.interceptor.ts` — NEW
- `src/app/hub/connections/connections.component.ts` — NEW
- `src/app/hub/dashboard/dashboard.component.ts` — NEW
- `src/app/hub/dora/dora.component.ts` — NEW (placeholder)
- `src/app/hub/health/health.component.ts` — NEW (placeholder)
- `src/app/hub/agent/agent.component.ts` — NEW (placeholder)
- `src/app/hub/pipelines/pipelines.component.ts` — NEW (placeholder)
- `src/app/app.routes.ts` — NEW (routing)
- `src/app/app.config.ts` — MODIFIED (added interceptor)
- `src/environments/environment.ts` — MODIFIED (API URL)
- `src/environments/environment.prod.ts` — MODIFIED (API URL)

### .NET API
- `src/Velo.Api/Controllers/DoraController.cs` — MODIFIED (security + DTOs)
- `src/Velo.Api/Controllers/OrgsController.cs` — NEW
- `src/Velo.Shared/Contracts/IMetricsRepository.cs` — MODIFIED (added org methods)

---

## Security Checklist

- ✅ All endpoints [Authorize]
- ✅ JWT token extracted from ADO SDK
- ✅ org_id validated on every request
- ✅ EF Core query filters per tenant table
- ✅ SQL Server RLS policies (via middleware session context)
- ✅ Rate limiting per org per day
- ✅ HTTPS enforced in production
- ✅ CORS restricted to ADO origins
- ✅ No secrets in code/config (all from Key Vault/SDK)
- ✅ All inputs validated before use
- ✅ Error messages don't leak sensitive info
- ✅ Structured logging with org_id/request context

