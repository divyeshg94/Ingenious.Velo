# GitHub Copilot Instructions — Velo

Velo is an open-source, AI-powered engineering intelligence platform built natively for Azure DevOps. It computes all five 2026 DORA metrics from pipeline data and provides a Foundry AI agent for pipeline optimization. The product is delivered as a native Azure DevOps extension on the Visual Studio Marketplace.

---

## Repository Layout

```
src/Velo.Extension/     Angular 19 Azure DevOps extension (Hub, Widgets, Pipeline Task)
src/Velo.Api/           ASP.NET Core 9 REST API on Azure Container Apps
src/Velo.Functions/     Azure Functions v4 — event ingestion and metrics computation
src/Velo.Agent/         Microsoft Foundry AI Agent for pipeline intelligence
src/Velo.Shared/        Shared EF Core entities and repository contracts (referenced by all)
infra/                  Bicep IaC — modules + dev/staging/prod params
db/migrations/          SQL Server migration scripts (001–006, run in order)
docs/                   Architecture, API, extension, and contributing docs
```

---

## Language and Runtime Versions

- C# 13, .NET 9, nullable enabled, implicit usings enabled
- TypeScript 5.6 with `"strict": true`
- Angular 19 (standalone components)
- Azure Functions v4 isolated worker (.NET 9)
- SQL Server / Azure SQL (T-SQL)
- Bicep (latest)

---

## C# Coding Standards

### General

- Use **primary constructors** for dependency injection in all classes.
  ```csharp
  // Correct
  public class DoraService(VeloDbContext db, ILogger<DoraService> logger) : IDoraService { }

  // Wrong — never use field injection or constructor body assignment
  public class DoraService : IDoraService
  {
      private readonly VeloDbContext _db;
      public DoraService(VeloDbContext db) { _db = db; }
  }
  ```

- Always pass `CancellationToken` as the last parameter to every `async` method.
- Never use `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()` — always `await`.
- Prefer `record` types for DTOs, request/response objects, and value objects.
- Use `async`/`await` end-to-end. Never mix synchronous and asynchronous code.

### Naming

| Element | Convention | Example |
|---------|-----------|---------|
| Classes, interfaces | PascalCase | `DoraService`, `IDoraService` |
| Methods | PascalCase | `GetMetricsAsync` |
| Private fields | `_camelCase` | `_db` |
| Local variables, params | camelCase | `orgId`, `cancellationToken` |
| Constants | PascalCase | `MaxTokenBudget` |
| C# files | PascalCase | `DoraService.cs` |

### Services

- Define the interface and its implementation in the same `.cs` file.
- Register all services as `Scoped` unless there is an explicit reason for `Singleton` or `Transient`.
- Never put business logic in controllers — delegate everything to a service.

### Error Handling

- Use domain-specific exceptions for expected failure conditions.
- Return `ActionResult<T>` from controllers — never `IActionResult` with anonymous types.
- Log with structured properties, not string interpolation:
  ```csharp
  // Correct
  logger.LogInformation("Processing run: PipelineId={PipelineId} OrgId={OrgId}", run.PipelineId, orgId);

  // Wrong
  logger.LogInformation($"Processing run: {run.PipelineId} {orgId}");
  ```

---

## Multi-Tenancy — Critical Pattern

Every database table that stores customer data has an `org_id` column. All queries **must** be scoped to the current org. There are two enforcement layers:

### Layer 1 — EF Core Query Filter (application layer)

`VeloDbContext` has a `CurrentOrgId` property set by `TenantResolutionMiddleware`. Every entity with `org_id` uses a global query filter:

```csharp
modelBuilder.Entity<PipelineRun>().HasQueryFilter(r => r.OrgId == CurrentOrgId);
```

Never bypass the query filter with `.IgnoreQueryFilters()` unless writing an explicit admin/cross-org operation, and only with a comment explaining why.

### Layer 2 — SQL Server Row-Level Security (database layer)

Migration `006_row_level_security.sql` creates RLS policies on all tenant tables using `SESSION_CONTEXT(N'org_id')`. Before any raw SQL (Dapper), set the session context:

```csharp
await connection.ExecuteAsync("EXEC sp_set_session_context N'org_id', @OrgId", new { OrgId = orgId });
```

**Never write a raw SQL query against a tenant table without setting session context first.**

### Resolving OrgId

`TenantResolutionMiddleware` extracts `org_id` from the Azure AD B2C token's `oid` claim and sets it on the scoped `VeloDbContext`. Do not re-read the org ID from the request in service or controller code — read it from `dbContext.CurrentOrgId`.

---

## Security — Non-Negotiable Rules

- **No secrets in code or config files.** All connection strings, API keys, and credentials come from Azure Key Vault via Managed Identity. Use `IConfiguration` which is backed by Key Vault references.
- **Managed Identity everywhere.** Never use connection strings with username/password for Azure SQL, Storage, or Key Vault. Use `Authentication=Active Directory Managed Identity` in the SQL connection string.
- **No `--no-verify` on git commits.** Never suggest bypassing pre-commit hooks.
- **Validate all inputs** at API boundaries. Never trust data from Azure DevOps service hooks without schema validation.
- Follow OWASP Top 10 — no SQL injection (use EF Core or parameterized Dapper queries), no XSS in Angular templates (use Angular's built-in sanitization, never `[innerHTML]` with user content).

---

## Azure Functions Patterns

### Triggers

- HTTP trigger for ADO service hooks: `ServiceHookTrigger` — route `velo/hooks/ado`, auth level `Function`.
- Timer trigger for metrics computation: `MetricsComputeTimer` — schedule read from `METRICS_COMPUTE_SCHEDULE` app setting (default: `0 0 * * * *` = hourly).
- Use `FunctionContext` for logging and dependency resolution, not static methods.

### Isolated Worker Model

Always use the .NET isolated worker model (`Microsoft.Azure.Functions.Worker`), not the in-process model (`Microsoft.Azure.WebJobs`). Entry point is `Program.cs` with `HostBuilder`.

### Service Hook Payload Handling

ADO sends service hooks for these events — handle all three:
- `build.complete` → normalize to `PipelineRunEvent`, persist to `pipeline_runs`
- `git.pullrequest.merged` → persist to `pull_requests` for lead time computation
- `workitem.updated` → persist state transitions for rework rate tracking

Return HTTP 200 immediately after writing to the database. Do not do AI or expensive computation inside the HTTP trigger — that belongs in the timer trigger.

---

## DORA Metrics Domain Knowledge

Velo computes all five 2026 DORA metrics. Understand what each measures before implementing:

| Metric | Definition | Data Source | Rating Thresholds |
|--------|-----------|-------------|------------------|
| Deployment Frequency | Deployments per day to production | `pipeline_runs` where `is_deployment = 1` | Elite: multiple/day, High: 1/day, Medium: 1/week, Low: <1/week |
| Lead Time for Changes | Time from first commit to production deployment | `pull_requests.created_at` → `pipeline_runs.finish_time` | Elite: <1h, High: <1d, Medium: <1w, Low: >1w |
| Change Failure Rate | % of deployments causing a rollback/hotfix | `rework_events` / `pipeline_runs` | Elite: 0-5%, High: 5-10%, Medium: 10-15%, Low: >15% |
| Mean Time to Restore | Time from failure detection to restoration | `pipeline_runs` failure → next success | Elite: <1h, High: <1d, Medium: <1w, Low: >1w |
| Rework Rate | % of deployments followed by a rework event within 48h | `rework_events` | Elite: <5%, High: 5-10%, Medium: 10-20%, Low: >20% |

**Deployment detection:** A `pipeline_run` is a deployment if:
1. `is_deployment = true` (set during normalization based on stage name patterns)
2. Stage names matching: `deploy`, `release`, `prod`, `production`, `publish` (case-insensitive)
3. Or explicitly tagged via the `Velo@1` pipeline task

Metrics computation happens in `MetricsEngine` — pure SQL aggregation, zero AI cost.

---

## Foundry AI Agent

### Architecture

`VeloAgent` in `src/Velo.Agent/` wraps the Microsoft Foundry Agent Framework. It is called by `AgentService` in `Velo.Api`. The agent has three tools:

- `PipelineAnalysisTool` — fetch YAML, build history, stage timing
- `CodeAnalysisTool` — PR size metrics, test stability trends, code churn
- `RecommendationTool` — structured optimization recommendations

### Cost Controls — Always Enforce

1. **Response caching by pipeline hash.** Before calling Foundry, compute SHA-256 of the pipeline YAML definition. Check `agent_interactions` for a recent cached response with the same `pipeline_hash`. Return cached if within TTL (`AgentConfig.ResponseCacheTtl`, default 6h). Write `cached = 1` to `agent_interactions`.

2. **Daily token budget enforcement.** Before any Foundry call, query `v_daily_token_usage` for the current org and date. If `total_tokens >= org.DailyTokenBudget`, return a `429 Too Many Requests` with a message explaining the free-tier limit. Premium orgs (`organizations.is_premium = 1`) bypass this check.

3. **Log every interaction.** Always write a row to `agent_interactions` with `input_tokens`, `output_tokens`, `latency_ms`, `cached`, and `tools_called` (JSON array).

### Tool Implementation Pattern

Each tool method must:
- Accept `orgId` and `projectId` — never query without these
- Set SQL `SESSION_CONTEXT` before any database access
- Return structured data the agent can reason over, not raw SQL result sets

---

## ASP.NET Core API Patterns

### Controller Design

```csharp
// Correct — thin controller, all logic in service
[ApiController]
[Route("api/[controller]")]
public class DoraController(IDoraService doraService) : ControllerBase
{
    [HttpGet("metrics")]
    public async Task<ActionResult<DoraMetrics>> GetMetrics(
        [FromQuery] string projectId,
        [FromQuery] int days = 30,
        CancellationToken cancellationToken = default)
        => Ok(await doraService.GetMetricsAsync(projectId, days, cancellationToken));
}
```

### Middleware Order (Program.cs)

The correct middleware order is:
1. `UseHttpsRedirection`
2. `UseCors("AdoExtension")`
3. `UseMiddleware<TenantResolutionMiddleware>` — must come before auth to set org context
4. `UseMiddleware<RateLimitMiddleware>` — checks token budget on `/api/agent/*`
5. `UseAuthorization`
6. `MapControllers`
5. `UseAuthorization`
6. `MapControllers`

### CORS

Only allow `https://dev.azure.com` and `https://*.visualstudio.com`. The Angular extension runs inside an ADO iframe from those origins. Never use `AllowAnyOrigin()`.

---

## Angular Extension Patterns

### VSS SDK Integration

The extension runs inside an Azure DevOps iframe. Always initialize the VSS SDK before doing anything else:

```typescript
import * as SDK from 'azure-devops-extension-sdk';

SDK.init({ loaded: false });
SDK.ready().then(() => {
  // safe to use ADO context here
  SDK.notifyLoadSucceeded();
});
```

### HTTP Calls

All HTTP calls go through services in `src/app/shared/services/`. Services use `HttpClient` with the base URL from `environment.apiBaseUrl`. Never call `HttpClient` directly from a component.

```typescript
// Correct — service in shared/services/
@Injectable({ providedIn: 'root' })
export class DoraService {
  constructor(private http: HttpClient) {}
  getMetrics(projectId: string): Observable<DoraMetrics> {
    return this.http.get<DoraMetrics>(`${environment.apiBaseUrl}/dora/metrics`, { params: { projectId } });
  }
}

// Wrong — never in a component
this.http.get('/api/dora/metrics').subscribe(...)
```

### Extension Entry Points

There are exactly three publishable components — do not add new top-level contributions without updating `vss-extension.json`:

1. **Hub** (`velo-hub`) — the full dashboard at `dist/index.html`
2. **Dashboard Widget** (`velo-dora-widget`) — compact DORA tile at `dist/widget.html`
3. **Pipeline Task** (`velo-pipeline-task`) — the `Velo@1` YAML task

### Standalone Components

Use standalone components (no `NgModule`):

```typescript
@Component({
  standalone: true,
  selector: 'velo-dora-dashboard',
  imports: [CommonModule, RouterModule],
  templateUrl: './dashboard.component.html',
})
export class DashboardComponent { }
```

---

## Database Patterns

### Entity Framework Core

- Always use `async` EF methods: `ToListAsync`, `FirstOrDefaultAsync`, `SaveChangesAsync`.
- Never use `DbContext` directly in controllers — only in services or repositories.
- Global query filters on `VeloDbContext` handle RLS at the application layer automatically — do not add manual `Where(r => r.OrgId == orgId)` clauses; they are redundant and misleading.
- **Always use EF Core migrations (Add-Migration)** for all database schema changes. Never create raw `.sql` migration files for table creation.

### Raw SQL (Dapper)

Use Dapper only for read-heavy aggregation queries where EF LINQ would generate inefficient SQL (e.g., DORA metric rollups). Pattern:

```csharp
await using var connection = new SqlConnection(connectionString);
await connection.ExecuteAsync(
    "EXEC sp_set_session_context N'org_id', @OrgId",
    new { OrgId = orgId });

var result = await connection.QueryAsync<DoraMetricRow>(
    "SELECT ... FROM pipeline_runs WHERE ...",
    new { OrgId = orgId, From = from, To = to });
```

### SQL Migrations

- Migration files are in `db/migrations/` numbered sequentially: `001_`, `002_`, etc.
- Always include an index on `(org_id, project_id, <timestamp_column> DESC)` for every new tenant table.
- When adding a new table that stores tenant data, add a corresponding RLS policy in a new migration (do not modify `006_row_level_security.sql`).

---

## Bicep IaC Patterns

- All resources use `SystemAssigned` Managed Identity — no service principals with passwords.
- Resource names follow the pattern: `velo-{component}-{environmentName}-{resourceSuffix}` where `resourceSuffix = uniqueString(resourceGroup().id)`.
- Outputs from modules that are consumed by other modules must be explicit `output` declarations — never hardcode resource names across modules.
- Azure SQL must always use the Serverless tier with `autoPauseDelay: 60` (60 minutes) to minimize idle cost.
- Container Apps must always have `minReplicas: 0` (scale to zero).

---

## What NOT to Do

- **Never** put secrets, connection strings, or PATs in any source file, `appsettings.json`, or `local.settings.json` that gets committed.
- **Never** use `AllowAnyOrigin()` in CORS configuration.
- **Never** call the Foundry agent without first checking the token budget and the response cache.
- **Never** add a raw SQL query against a tenant table without `SESSION_CONTEXT` set first.
- **Never** use `.IgnoreQueryFilters()` on EF queries without an inline comment explaining the cross-tenant need.
- **Never** do expensive work (AI calls, heavy computation) inside an HTTP-triggered Azure Function — put it in the timer trigger or a separate queue.
- **Never** use `[innerHTML]` in Angular templates with user-supplied content.
- **Never** add a new contribution to the ADO extension without updating `vss-extension.json`.
- **Never** implement a new DORA metric computation using AI — all five metrics are pure SQL aggregations.
- **Never** use `WidthType.PERCENTAGE` (this is a docx concern unrelated to this project — ignore if suggested by Copilot for non-docx files).
- **Never** create a new `NgModule` — all Angular components are standalone.

---

## Testing Expectations

- Every service method that contains business logic needs a unit test.
- `MetricsEngine` computations must have data-driven unit tests with known input datasets and expected DORA rating outputs.
- `EventNormalizer` must have tests covering all three ADO event types including malformed payloads.
- RLS must be covered by an integration test that proves `org_id = A` queries never return `org_id = B` rows.
- Azure Functions triggers must be tested via the `FunctionsTesting` test host — not by mocking the `HttpRequestData` directly.
- Angular services must have Jasmine unit tests mocking `HttpClient` with `HttpClientTestingModule`.

---

## Commit and PR Conventions

- Branch naming: `feature/`, `fix/`, `chore/`, `docs/`
- PR titles: imperative present tense — "Add deployment frequency computation" not "Added"
- One logical change per PR
- All CI checks (`dotnet test`, `npm test`, `npm run lint`) must pass before merge
- Squash merge to `main`

---

## Azure DevOps Extension Hub Configuration

- Configure the Velo extension hub to appear as its own top-level hub group in the Azure DevOps project sidebar, not nested under Pipelines or any other existing hub group.
