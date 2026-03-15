# Model C: Self-Hosted Backend (Enterprise Tier)

For government and regulated-industry customers with data residency requirements.

## How It Works

The same Azure DevOps extension is used — no reinstall required. The customer deploys the Velo backend to their own Azure subscription and configures a custom API base URL in the extension settings.

## Deployment Steps

1. **Clone the repo** and install prerequisites:
   - Azure CLI
   - Bicep CLI (`az bicep install`)
   - .NET 9 SDK

2. **Deploy infrastructure:**
   ```bash
   az login
   az group create -n velo-rg -l eastus2
   az deployment group create \
     -g velo-rg \
     -f infra/main.bicep \
     -p infra/parameters/prod.bicepparam
   ```

3. **Deploy the API and Functions** (or use the provided GitHub Actions workflow targeting your subscription).

4. **Configure the extension:** In Velo's **Connections** settings page, enter your API base URL.

## What the Customer Controls

- All pipeline data stays in their Azure subscription
- Foundry AI uses their Azure OpenAI resource (or they can disable the agent)
- They manage upgrades by pulling new releases and redeploying
