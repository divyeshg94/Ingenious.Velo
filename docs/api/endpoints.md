# Velo API Endpoints

Base URL: `https://api.getvelo.dev/api` (prod) | `https://localhost:7001/api` (dev)

All endpoints require a valid Azure AD B2C bearer token in the `Authorization` header.

## DORA Metrics

| Method | Path | Description |
|--------|------|-------------|
| GET | `/dora/metrics?projectId=&days=30` | Latest computed DORA metrics for a project |
| GET | `/dora/history?projectId=&days=90` | Historical DORA snapshots |

## Pipelines

| Method | Path | Description |
|--------|------|-------------|
| GET | `/pipelines?projectId=&page=1&pageSize=50` | Paginated pipeline run list |
| GET | `/pipelines/{pipelineId}/analysis` | AI-generated analysis for a pipeline |

## Team Health

| Method | Path | Description |
|--------|------|-------------|
| GET | `/health?projectId=` | Latest team health snapshot |

## Agent

| Method | Path | Description |
|--------|------|-------------|
| POST | `/agent/chat` | Send a message to the Pipeline Intelligence Agent |

**Request body:**
```json
{
  "projectId": "string",
  "message": "string",
  "history": [{ "role": "user|assistant", "content": "string" }]
}
```

## Connections

| Method | Path | Description |
|--------|------|-------------|
| POST | `/connections` | Register an ADO organization connection |
| DELETE | `/connections` | Remove the connection for the current org |

See `openapi.yml` for the full OpenAPI 3.1 specification.
