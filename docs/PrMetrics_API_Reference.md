# PR Metrics API - Request/Response Examples

## Endpoint 1: Get Average PR Size

### Request
```http
GET /api/pr-metrics/average-size?projectId=myproject&days=30
Authorization: Bearer {jwt_token}
```

### Query Parameters
- `projectId` (required): Project identifier, e.g., "my-project" or project GUID
- `days` (optional): Number of days to analyze. Default: 30. Range: 1-365

### Response (200 OK)
```json
{
  "orgId": "contoso",
  "projectId": "myproject",
  "periodStart": "2024-02-29T15:30:00Z",
  "periodEnd": "2024-03-30T15:30:00Z",
  "totalPrCount": 42,
  "averageFilesChanged": 8,
  "averageLinesAdded": 245,
  "averageLinesDeleted": 118,
  "averageTotalChanges": 363,
  "averageReviewCycleDurationMinutes": 240,
  "approvalRate": 85.71,
  "averageReviewerCount": 2.1,
  "computedAt": "2024-03-30T16:00:00Z"
}
```

### Response Fields
| Field | Type | Description |
|-------|------|-------------|
| `orgId` | string | Azure DevOps organization |
| `projectId` | string | Project identifier |
| `periodStart` | string (ISO 8601) | Start of analysis period |
| `periodEnd` | string (ISO 8601) | End of analysis period |
| `totalPrCount` | int | Number of completed PRs in period |
| `averageFilesChanged` | int | Mean files modified per PR |
| `averageLinesAdded` | int | Mean lines added per PR |
| `averageLinesDeleted` | int | Mean lines deleted per PR |
| `averageTotalChanges` | int | averageLinesAdded + averageLinesDeleted |
| `averageReviewCycleDurationMinutes` | int | Mean minutes from PR creation to first approval |
| `approvalRate` | decimal | Percentage of PRs with at least one approval (0-100) |
| `averageReviewerCount` | double | Mean number of reviewers per PR |
| `computedAt` | string (ISO 8601) | Timestamp when metrics were calculated |

### Error Responses

**400 Bad Request** - Invalid parameters:
```json
{
  "error": "days must be between 1 and 365"
}
```

**401 Unauthorized** - Missing/invalid token:
```json
{
  "error": "Unauthorized"
}
```

**500 Internal Server Error** - Calculation failure:
```json
{
  "error": "Failed to calculate PR metrics"
}
```

### cURL Example
```bash
curl -X GET \
  "https://api.velo.dev/api/pr-metrics/average-size?projectId=myproject&days=30" \
  -H "Authorization: Bearer eyJ0eXAiOiJKV1QiLCJhbGc..." \
  -H "Accept: application/json"
```

### PowerShell Example
```powershell
$headers = @{
    "Authorization" = "Bearer $jwt_token"
    "Accept" = "application/json"
}

$response = Invoke-RestMethod `
  -Method Get `
  -Uri "https://api.velo.dev/api/pr-metrics/average-size?projectId=myproject&days=30" `
  -Headers $headers

Write-Host "Avg PR Size: $($response.averageTotalChanges) lines"
Write-Host "Approval Rate: $($response.approvalRate)%"
```

---

## Endpoint 2: Get PR Size Distribution

### Request
```http
GET /api/pr-metrics/distribution?projectId=myproject&days=30
Authorization: Bearer {jwt_token}
```

### Query Parameters
- `projectId` (required): Project identifier
- `days` (optional): Number of days. Default: 30. Range: 1-365

### Response (200 OK)
```json
{
  "smallPrs": 18,
  "mediumPrs": 22,
  "largePrs": 6,
  "extraLargePrs": 2
}
```

### Response Fields
| Field | Type | Description |
|-------|------|-------------|
| `smallPrs` | int | Number of PRs with 0-100 lines changed |
| `mediumPrs` | int | Number of PRs with 101-500 lines changed |
| `largePrs` | int | Number of PRs with 501-1000 lines changed |
| `extraLargePrs` | int | Number of PRs with 1000+ lines changed |

### Size Bucket Definitions
```
Small:       0 -  100 lines  (Quick, focused changes)
Medium:    101 -  500 lines  (Standard PR size)
Large:     501 - 1000 lines  (Complex changes)
XtraLarge: 1000+     lines   (Major refactors/features)
```

### JavaScript/TypeScript Example
```typescript
const projectId = 'myproject';
const days = 30;

const response = await fetch(
  `/api/pr-metrics/distribution?projectId=${projectId}&days=${days}`,
  {
    headers: { 'Authorization': `Bearer ${token}` }
  }
);

const dist = await response.json();

console.log(`Small PRs (0-100): ${dist.smallPrs}`);
console.log(`Medium PRs (101-500): ${dist.mediumPrs}`);
console.log(`Large PRs (501-1000): ${dist.largePrs}`);
console.log(`Extra Large PRs (1000+): ${dist.extraLargePrs}`);

// Calculate percentages
const total = dist.smallPrs + dist.mediumPrs + dist.largePrs + dist.extraLargePrs;
console.log(`Small: ${((dist.smallPrs / total) * 100).toFixed(1)}%`);
console.log(`Medium: ${((dist.mediumPrs / total) * 100).toFixed(1)}%`);
console.log(`Large: ${((dist.largePrs / total) * 100).toFixed(1)}%`);
console.log(`Extra Large: ${((dist.extraLargePrs / total) * 100).toFixed(1)}%`);
```

---

## Endpoint 3: Get Top Reviewers

### Request
```http
GET /api/pr-metrics/reviewers?projectId=myproject&topCount=10&days=30
Authorization: Bearer {jwt_token}
```

### Query Parameters
- `projectId` (required): Project identifier
- `topCount` (optional): Number of reviewers to return. Default: 10. Range: 1-100
- `days` (optional): Number of days. Default: 30. Range: 1-365

### Response (200 OK)
```json
[
  {
    "reviewerName": "Alice Smith",
    "prReviewCount": 45,
    "approvalCount": 42,
    "rejectionCount": 3
  },
  {
    "reviewerName": "Bob Johnson",
    "prReviewCount": 38,
    "approvalCount": 35,
    "rejectionCount": 2
  },
  {
    "reviewerName": "Carol Davis",
    "prReviewCount": 32,
    "approvalCount": 29,
    "rejectionCount": 3
  }
]
```

### Response Array Items
| Field | Type | Description |
|-------|------|-------------|
| `reviewerName` | string | Display name of the reviewer |
| `prReviewCount` | int | Total PRs this reviewer participated in |
| `approvalCount` | int | Number of approvals given |
| `rejectionCount` | int | Number of rejections/concerns raised |

### C# / .NET Example
```csharp
using (var client = new HttpClient())
{
    var token = "eyJ0eXAiOiJKV1QiLCJhbGc...";
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    
    var url = "https://api.velo.dev/api/pr-metrics/reviewers?" +
              "projectId=myproject&topCount=10&days=30";
    
    var response = await client.GetAsync(url);
    var json = await response.Content.ReadAsStringAsync();
    
    var reviewers = JsonSerializer.Deserialize<ReviewerInsightsDto[]>(json);
    
    foreach (var reviewer in reviewers)
    {
        var approvalRate = (decimal)reviewer.ApprovalCount / 
                          reviewer.PrReviewCount * 100;
        
        Console.WriteLine($"{reviewer.ReviewerName}:");
        Console.WriteLine($"  Reviews: {reviewer.PrReviewCount}");
        Console.WriteLine($"  Approvals: {reviewer.ApprovalCount}");
        Console.WriteLine($"  Rejections: {reviewer.RejectionCount}");
        Console.WriteLine($"  Approval Rate: {approvalRate:F1}%");
    }
}
```

---

## Batch Request Example

### Get All PR Metrics at Once
```javascript
async function getPrMetricsDashboard(projectId, days = 30) {
  const token = sessionStorage.getItem('auth_token');
  const headers = {
    'Authorization': `Bearer ${token}`,
    'Accept': 'application/json'
  };
  
  const baseUrl = 'https://api.velo.dev/api/pr-metrics';
  
  try {
    const [metrics, dist, reviewers] = await Promise.all([
      fetch(`${baseUrl}/average-size?projectId=${projectId}&days=${days}`, { headers })
        .then(r => r.json()),
      fetch(`${baseUrl}/distribution?projectId=${projectId}&days=${days}`, { headers })
        .then(r => r.json()),
      fetch(`${baseUrl}/reviewers?projectId=${projectId}&topCount=10&days=${days}`, { headers })
        .then(r => r.json())
    ]);
    
    return { metrics, distribution: dist, reviewers };
  } catch (error) {
    console.error('Failed to fetch PR metrics:', error);
    throw error;
  }
}

// Usage
const dashboard = await getPrMetricsDashboard('myproject', 30);
console.log(dashboard.metrics);
console.log(dashboard.distribution);
console.log(dashboard.reviewers);
```

---

## Rate Limiting & Performance Notes

### Request Limits
- No explicit rate limiting on these endpoints
- Recommended: 1 request per dashboard refresh (max once per minute)
- Batch calls using Promise.all() is preferred over serial requests

### Response Times
- Average-size: 50-200ms
- Distribution: 30-100ms
- Reviewers: 40-150ms
- Batch: 150-300ms total

### Caching Strategy (Client-Side)
```javascript
class CachedPrMetricsService {
  constructor(cacheDurationMs = 300000) { // 5 minutes
    this.cache = new Map();
    this.cacheDuration = cacheDurationMs;
  }
  
  async getMetrics(projectId, days) {
    const cacheKey = `${projectId}:${days}`;
    const cached = this.cache.get(cacheKey);
    
    if (cached && Date.now() - cached.timestamp < this.cacheDuration) {
      return cached.data;
    }
    
    const data = await fetch(
      `/api/pr-metrics/average-size?projectId=${projectId}&days=${days}`
    ).then(r => r.json());
    
    this.cache.set(cacheKey, { data, timestamp: Date.now() });
    return data;
  }
}
```

---

## Error Handling Examples

### JavaScript/TypeScript
```typescript
async function fetchPrMetrics(projectId: string, days: number) {
  try {
    const response = await fetch(
      `/api/pr-metrics/average-size?projectId=${projectId}&days=${days}`,
      {
        method: 'GET',
        headers: {
          'Authorization': `Bearer ${getAuthToken()}`,
          'Accept': 'application/json'
        }
      }
    );

    if (!response.ok) {
      const error = await response.json();
      throw new Error(error.error || `HTTP ${response.status}`);
    }

    return await response.json();
  } catch (error) {
    console.error('Failed to fetch PR metrics:', error.message);
    throw error;
  }
}
```

### C# / .NET
```csharp
public async Task<PrSizeMetricsDto> GetPrMetricsAsync(
    string projectId, int days, CancellationToken ct)
{
    try
    {
        var url = $"https://api.velo.dev/api/pr-metrics/average-size" +
                  $"?projectId={Uri.EscapeDataString(projectId)}&days={days}";
        
        var response = await _httpClient.GetAsync(url, ct);
        
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"API Error {(int)response.StatusCode}: {error}");
        }
        
        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<PrSizeMetricsDto>(json)!;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to fetch PR metrics for {ProjectId}", projectId);
        throw;
    }
}
```

---

## Testing the Endpoints

### Quick Test Using Postman

1. **Create new request** (POST → GET)
2. **URL**: `https://dev-api.velo.dev/api/pr-metrics/average-size`
3. **Query Params**:
   - Key: `projectId`, Value: `myproject`
   - Key: `days`, Value: `30`
4. **Headers**:
   - Key: `Authorization`, Value: `Bearer {your_jwt_token}`
5. **Send** → View response

### Using Azure CLI
```bash
# Get auth token
$token = az account get-access-token --query accessToken -o tsv

# Call API
curl -X GET \
  "https://api.velo.dev/api/pr-metrics/average-size?projectId=myproject&days=30" \
  -H "Authorization: Bearer $token" \
  -H "Accept: application/json"
```

---

**Last Updated**: 2024-03-30  
**API Version**: v1  
**Status**: Stable
