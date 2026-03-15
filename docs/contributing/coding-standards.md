# Coding Standards

## C# (.NET)

- Target .NET 9, C# 13, nullable enabled, implicit usings enabled
- Use primary constructors for dependency injection
- `async`/`await` throughout — no `.Result` or `.Wait()`
- Pass `CancellationToken` to all async methods
- Interface + implementation in the same file for services (keeps files concise)
- No connection strings or secrets in code — use `IConfiguration` backed by Key Vault

## TypeScript (Angular)

- Angular 19 with standalone components
- Strict TypeScript (`"strict": true`)
- `HttpClient` via service classes — no direct HTTP calls in components
- Use `async` pipe in templates over manual subscriptions
- Models in `shared/models/`, services in `shared/services/`

## Naming

| Item | C# | TypeScript |
|------|----|------------|
| Classes | `PascalCase` | `PascalCase` |
| Interfaces | `IPascalCase` | `PascalCase` (no `I` prefix in TS) |
| Methods | `PascalCase` | `camelCase` |
| Properties | `PascalCase` | `camelCase` |
| Files | `PascalCase.cs` | `kebab-case.ts` |

## SQL

- Table names: `snake_case` (plural)
- Column names: `snake_case`
- Always include `org_id` for multi-tenant tables
- Every table needs a `created_at` or equivalent timestamp column

## Git

- Branch naming: `feature/`, `fix/`, `chore/`
- PR titles: imperative present tense — "Add DORA metrics endpoint" not "Added"
- Squash merge to main
