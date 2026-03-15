# Local Development Setup

## Prerequisites

| Tool | Version | Purpose |
|------|---------|---------|
| .NET SDK | 9.x | Backend projects |
| Node.js | 20.x | Angular extension |
| Azure Functions Core Tools | 4.x | Local Functions runtime |
| Azure CLI | latest | IaC and resource management |
| SQL Server / Azure SQL Edge | latest | Local database (Docker recommended) |

## Quick Start

```bash
# Clone
git clone https://github.com/divyeshg94/velo.git
cd velo

# Backend
dotnet restore
dotnet build

# Start API
cd src/Velo.Api
dotnet run

# Start Functions (separate terminal)
cd src/Velo.Functions
func start

# Extension (separate terminal)
cd src/Velo.Extension
npm install
npm start
```

## Local Database

```bash
# Start SQL Server via Docker
docker run -e ACCEPT_EULA=Y -e SA_PASSWORD=Velo@Dev123 \
  -p 1433:1433 --name velo-sql \
  -d mcr.microsoft.com/mssql/server:2022-latest

# Apply migrations
sqlcmd -S localhost -U sa -P Velo@Dev123 -d master -i db/migrations/001_initial_schema.sql
sqlcmd -S localhost -U sa -P Velo@Dev123 -d VeloDev -i db/migrations/002_dora_metrics.sql
# ... repeat for remaining migrations
```

## Environment Variables

Copy `appsettings.Development.json` and fill in your local values. Never commit secrets — use `dotnet user-secrets` for sensitive values:

```bash
cd src/Velo.Api
dotnet user-secrets set "ConnectionStrings:VeloDb" "Server=localhost;..."
```
