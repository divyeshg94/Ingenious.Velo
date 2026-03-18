# Local Testing Quick Start

## To Test Locally Without Publishing to Azure DevOps

### 1. Start Backend API

```bash
cd src/Velo.Api
dotnet run
```

Will run on `https://localhost:5001`

### 2. Start Angular Extension

```bash
cd src/Velo.Extension
ng serve --open
```

Will open `http://localhost:4200`

### 3. You'll See a YELLOW DEV BANNER

This means you're running in **local development mode** (not inside an ADO iframe).

Click the **⚙️ Dev Settings** button to configure:
- Mock Org ID
- Mock User ID  
- Mock Token
- API Base URL (default: `http://localhost:5001`)

### 4. Test Navigation

- Click through Dashboard, DORA Metrics, etc.
- All routes work without ADO context
- Theme switching works
- **No SDK errors in console** ✅

### 5. Test Connections (Requires API)

- Go to **Connections** tab
- Enter org URL: `https://dev.azure.com/your-org`
- Click **Connect**
- Check browser Network tab (F12) - should see POST to `/api/orgs/connect`

### Why You Got SDK Errors Locally

The errors you saw are **expected**:
```
No handler found on any channel for message
The registered object DevOps.HostControl could not be found
```

**Reason**: Azure DevOps SDK only works inside an ADO iframe. Locally, we use a **mock SDK** that simulates ADO behavior.

**Check if mock SDK is working** (in browser console):
```
[SDK] Running locally - using mock SDK ✅
```

### When to Publish to Azure DevOps

1. ✅ After testing locally (all routes work, no console errors)
2. ✅ After testing API calls (network requests work)
3. ✅ When ready for real ADO testing

To publish:
```bash
cd src/Velo.Extension
npm run build
tfx extension create
# Upload generated VSIX to Azure DevOps marketplace
```

---

## TL;DR

**You can test 90% locally without publishing!**

| Feature | Testable Locally? |
|---------|---|
| Navigation | ✅ Yes |
| Routing | ✅ Yes |
| Styling/Themes | ✅ Yes |
| API Connectivity | ✅ Yes (if API running) |
| Org Connection | ✅ Yes (if API running) |
| DORA Dashboard | ✅ Yes (if API + data) |
| ADO SDK Features | ❌ No (requires iframe) |

**Summary**: You're good to go! The SDK errors are expected and handled. Use the Dev Settings to configure your local environment and test the features.
