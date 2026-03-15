# Azure DevOps Service Hook Configuration

Velo ingests data via ADO service hooks. Configure these in **Project Settings → Service Hooks → Add subscription**.

## Required Hooks

| Event | Trigger | Velo Endpoint |
|-------|---------|--------------|
| Build completed | All pipelines | `POST https://velo-functions-{env}.azurewebsites.net/api/velo/hooks/ado` |
| Pull request merged | All repos | same endpoint |
| Work item updated | All work items | same endpoint |

## Authentication

Use **HMAC shared secret** or **Basic Auth** with the Azure Function key. The Function key is stored in Key Vault — retrieve it with:

```bash
az keyvault secret show --vault-name velo-kv-dev-<suffix> --name service-hook-key --query value -o tsv
```

## Verifying Hook Delivery

1. Check Application Insights → `customEvents` filter on `ServiceHookReceived`
2. Query `pipeline_runs` table to confirm data is flowing
3. Use ADO **Service hooks** page → **History** tab to see delivery status and retry failed hooks
