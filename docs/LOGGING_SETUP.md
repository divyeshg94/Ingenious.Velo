# Serilog - Structured Logging Configuration

## Required NuGet Packages

Add these to `src/Velo.Api/Velo.Api.csproj`:

```xml
<!-- Serilog Core -->
<PackageReference Include="Serilog" Version="4.*" />
<PackageReference Include="Serilog.AspNetCore" Version="8.*" />

<!-- Serilog Sinks -->
<PackageReference Include="Serilog.Sinks.Console" Version="5.*" />
<PackageReference Include="Serilog.Sinks.MSSqlServer" Version="6.*" />

<!-- Serilog Enrichers -->
<PackageReference Include="Serilog.Enrichers.Environment" Version="3.*" />
<PackageReference Include="Serilog.Enrichers.Thread" Version="4.*" />
<PackageReference Include="Serilog.Enrichers.Context" Version="4.*" />
```

## Installation Command

```bash
cd src/Velo.Api
dotnet add package Serilog --version 4.*
dotnet add package Serilog.AspNetCore --version 8.*
dotnet add package Serilog.Sinks.Console --version 5.*
dotnet add package Serilog.Sinks.MSSqlServer --version 6.*
dotnet add package Serilog.Enrichers.Environment --version 3.*
dotnet add package Serilog.Enrichers.Thread --version 4.*
dotnet add package Serilog.Enrichers.Context --version 4.*
```

## What Gets Logged

1. **All HTTP Requests/Responses** — Method, path, status code, duration, host
2. **Authentication Events** — Successful logins, failed attempts, permission denials
3. **Rate Limiting** — Token budget tracking, quota exceeded alerts
4. **Multi-Tenancy** — org_id resolution, cross-org access attempts
5. **Security Events** — Unauthorized access, suspicious patterns, RLS violations
6. **Application Errors** — Full exception stack traces with context
7. **Audit Trail** — Who did what, when, from where (userId, orgId, timestamp, correlationId)

## Database Schema

All logs persist to the `dbo.Logs` table with these columns:
- `MessageTemplate` — Log message pattern
- `Level` — Debug, Information, Warning, Error, Fatal
- `TimeStamp` — UTC timestamp
- `Exception` — Full exception if present
- `LogEvent` — Complete JSON event
- `OrgId` — Organization context (protected by RLS)
- `UserId` — User identity
- `CorrelationId` — Request trace ID
- `RequestPath` — HTTP path
- `RequestMethod` — HTTP verb
- `StatusCode` — HTTP response code
- `DurationMs` — Request duration
- `Properties` — Structured properties JSON

All with indexes for performance:
- TimeStamp (for time-based queries)
- OrgId + TimeStamp (for org-specific audit trails)
- Level + TimeStamp (for error analysis)
- CorrelationId (for request tracing)

## Querying Logs

```sql
-- Find all errors for an org in the last 24 hours
SELECT MessageTemplate, Level, TimeStamp, Exception, Properties
FROM dbo.Logs
WHERE OrgId = 'your-org-id' AND Level = 'Error' AND TimeStamp > DATEADD(hour, -24, GETUTCDATE())
ORDER BY TimeStamp DESC;

-- Trace a specific request
SELECT MessageTemplate, Level, TimeStamp, Properties
FROM dbo.Logs
WHERE CorrelationId = 'correlation-id-here'
ORDER BY TimeStamp;

-- Find security events
SELECT MessageTemplate, TimeStamp, UserId, OrgId, RequestPath, Properties
FROM dbo.Logs
WHERE MessageTemplate LIKE '%SECURITY%' OR Level = 'Warning'
ORDER BY TimeStamp DESC;

-- Audit trail for a specific user
SELECT MessageTemplate, TimeStamp, OrgId, RequestPath, StatusCode, Properties
FROM dbo.Logs
WHERE UserId = 'user-id-here'
ORDER BY TimeStamp DESC;
```

## Log Levels

- **Debug** — Detailed diagnostic info (disabled in production)
- **Information** — Audit events, general flow (DORA metrics fetched, org connected, etc.)
- **Warning** — Potentially harmful situations (rate limit approaching, unusual patterns)
- **Error** — Error conditions (database failures, invalid requests)
- **Fatal** — Application shutdown

