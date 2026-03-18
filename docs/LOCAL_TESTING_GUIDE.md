# Local Testing Guide - Velo Extension

## How to Test Locally Without Publishing to Azure DevOps

### Prerequisites

1. **Backend API running locally**
   ```bash
   cd src/Velo.Api
   dotnet run
   # API runs on https://localhost:5001
   ```

2. **Angular extension dev server**
   ```bash
   cd src/Velo.Extension
   ng serve --open
   # Runs on http://localhost:4200
   ```

### Step 1: Start the Dev Environment

Open two terminal windows:

**Terminal 1 - Backend API:**
```bash
cd src/Velo.Api
dotnet run
```
Expected output:
```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: https://localhost:5001
info: Microsoft.Hosting.Lifetime[0]
      Application started. Press Ctrl+C to stop.
```

**Terminal 2 - Angular Extension:**
```bash
cd src/Velo.Extension
npm install  # First time only
ng serve --open
```
Expected output:
```
✔ Compiled successfully.
✔ Build complete. Watching for file changes...
⠙ Generating browser application bundles...
** Angular Live Development Server is listening on localhost:4200 **
```

### Step 2: Configure Local Settings

When you open `http://localhost:4200`, you'll see a **YELLOW DEV BANNER** at the top with a **⚙️ Dev Settings** button.

Click it and configure:
- **Mock Org ID**: `local-org-dev` (or your Azure org ID)
- **Mock User ID**: `local-user-dev` (or your Azure user ID)
- **Mock Token**: `mock-token-for-local-dev`
- **API Base URL**: `http://localhost:5001`

Click **Save Settings** - the page will reload with your settings applied.

### Step 3: Test Components

#### Test Navigation (No Backend Required)
- Navigate between tabs: Dashboard, DORA Metrics, Team Health, etc.
- Theme switching works even without ADO context
- Check browser console - should NOT have SDK errors

#### Test Organization Connection (Requires Backend)
1. Go to **Connections** tab
2. Enter org URL: `https://dev.azure.com/local-org-dev`
3. Click **Connect Organization**
4. Watch backend logs for request:
   ```
   info: Velo.Api.Controllers.OrgsController[0]
        AUDIT: Connecting organization - OrgId: local-org-dev, OrgUrl: ...
   ```

#### Test DORA Dashboard (Requires Data)
1. Go to **Dashboard** tab
2. Check browser network tab (F12) - should see GET `/api/dora/latest`
3. Backend should log:
   ```
   info: Velo.Api.Controllers.DoraController[0]
        AUDIT: Fetching latest DORA metrics - OrgId: local-org-dev, ProjectId: ...
   ```

### Troubleshooting

#### ❌ "No handler found on any channel for message"
**This is NORMAL for local testing!** The extension detects it's running outside ADO and uses mock SDK.

Check: Open browser console (F12) - should see `[SDK] Running locally - using mock SDK`

#### ❌ API Returns 401 Unauthorized
**Reason**: Mock token is fake (not valid JWT for your API)

**Fix**: 
1. Update `appsettings.json` to skip token validation in dev mode
   ```json
   "JwtOptions": {
     "ValidateIssuer": false,
     "ValidateAudience": false,
     "ValidateLifetime": false
   }
   ```
2. Or: Set `ASPNETCORE_ENVIRONMENT=Development`

#### ❌ CORS Error: "Access to XMLHttpRequest blocked"
**Fix**: Backend `Program.cs` already has CORS configured for `localhost:*`

Check if it includes `http://localhost:4200`:
```csharp
policy.WithOrigins("https://dev.azure.com", "https://*.visualstudio.com", "http://localhost:*")
```

#### ❌ Database Connection Error
**Fix**: Update `appsettings.json` with your local SQL Server:
```json
"ConnectionStrings": {
  "VeloDb": "Server=.;Database=Velo;Authentication=Active Directory Integrated;"
}
```

### Browser Console Logs to Expect

**Good (Local Mode):**
```
[SDK] Running locally - using mock SDK
[SDK] Initialized with options: {loaded: false}
[SDK] SDK ready
[SDK] Load succeeded
[App] Velo extension bootstrapped successfully
[Auth Interceptor] Using local mock token
```

**Bad (SDK Errors):**
```
No handler found on any channel for message...
The registered object DevOps.HostControl could not be found
```

### Testing Workflow

| Scenario | Steps | Expected |
|----------|-------|----------|
| **Component Navigation** | Click tabs in nav bar | Routes work, no console errors |
| **Dark Theme** | Press F12, DevTools → right-click velo-root → inspect element, check `data-theme="dark"` | Theme CSS applied |
| **Connect Organization** | Enter org URL, click connect | POST /api/orgs/connect succeeds |
| **View DORA Metrics** | Click Dashboard after connecting org | GET /api/dora/latest returns data (or 404 if no data) |
| **Error Handling** | Disconnect network, try API call | Should display user-friendly error, not blank screen |

### Publishing vs Local Testing

| Feature | Local (ng serve) | Published (VSIX) |
|---------|---|---|
| **Code changes** | Hot reload (instant) | Requires rebuild & publish |
| **Network calls** | Can use localhost | Must use HTTPS public endpoint |
| **Debugging** | Browser DevTools, Chrome debugger | Browser DevTools only |
| **ADO Integration** | Mocked/disabled | Full SDK integration |
| **Performance** | Unminified (slower) | Minified (faster) |

### When to Publish to Azure DevOps

1. ✅ After testing locally and confirming no console errors
2. ✅ After testing API connectivity (Connections tab works)
3. ✅ After testing error handling (network off, API down, etc.)
4. ✅ When ready to test inside actual ADO iframe
5. ✅ Before code review/PR merge

### Quick Start Script

```bash
#!/bin/bash
# Run both API and extension in development

# Terminal 1
cd src/Velo.Api && dotnet run &

# Terminal 2
cd src/Velo.Extension && ng serve --open
```

---

## Next Steps

After successful local testing:
1. Run `npm run build` for production build
2. Run `tfx extension create` to generate VSIX
3. Upload to [Azure DevOps Marketplace](https://marketplace.visualstudio.com/)
4. Install in your organization for final testing

