using System.ClientModel;
using System.Text.Json;
using Velo.Agent;

// Live connectivity check — exercises the EXACT same code path as
// AgentConfigService.TestConnectionAsync (FoundryClientFactory.BuildAgent + AIAgent.RunAsync)
// against a real Foundry resource, outside the API/DB. Run before every checkin that touches
// the Foundry agent path (see FoundryClientFactory, VeloAgent, IAgentConfigService, IAgentService)
// so a working build never masks a broken live integration — see the 2026-08-15 incident where
// every RunAsync catch clause silently mismatched the real exception type.
//
// Config resolution, in order:
//   1. File path passed as the first argument
//   2. %USERPROFILE%\.velo-foundry-test.json (local dev — gitignored, holds real secrets)
//   3. Environment variables FOUNDRY_ENDPOINT / FOUNDRY_DEPLOYMENT_NAME / FOUNDRY_TENANT_ID /
//      FOUNDRY_CLIENT_ID / FOUNDRY_CLIENT_SECRET (CI — populated from pipeline secrets)
//
// Config file shape:
// {
//   "FoundryEndpoint": "https://<hub>.services.ai.azure.com/api/projects/<project>",
//   "DeploymentName": "gpt-5.2",
//   "TenantId": "...",
//   "ClientId": "...",
//   "ClientSecret": "..."
// }
// Omit TenantId/ClientId/ClientSecret to test Managed Identity (DefaultAzureCredential) instead.
//
// FoundryEndpoint and DeploymentName are printed in full (not secrets). TenantId/ClientId are
// masked previews. ClientSecret is never printed or logged anywhere — so this is safe to run in
// CI logs or share with a human.

var configPath = args.Length > 0
    ? args[0]
    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".velo-foundry-test.json");

TestConfig config;

if (File.Exists(configPath))
{
    var json = await File.ReadAllTextAsync(configPath);
    try
    {
        config = JsonSerializer.Deserialize<TestConfig>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new JsonException("Config file deserialized to null.");
    }
    catch (JsonException ex)
    {
        Console.WriteLine($"Config file at {configPath} is not valid JSON: {ex.Message}");
        return 1;
    }
}
else if (Environment.GetEnvironmentVariable("FOUNDRY_ENDPOINT") is { Length: > 0 } envEndpoint)
{
    config = new TestConfig(
        envEndpoint,
        Environment.GetEnvironmentVariable("FOUNDRY_DEPLOYMENT_NAME"),
        Environment.GetEnvironmentVariable("FOUNDRY_TENANT_ID"),
        Environment.GetEnvironmentVariable("FOUNDRY_CLIENT_ID"),
        Environment.GetEnvironmentVariable("FOUNDRY_CLIENT_SECRET"));
}
else
{
    Console.WriteLine($"No config found — neither {configPath} nor FOUNDRY_ENDPOINT env var is set.");
    Console.WriteLine();
    Console.WriteLine("Local dev: create the file above with:");
    Console.WriteLine("""
        {
          "FoundryEndpoint": "https://<hub>.services.ai.azure.com/api/projects/<project>",
          "DeploymentName": "gpt-5.2",
          "TenantId": "...",
          "ClientId": "...",
          "ClientSecret": "..."
        }
        """);
    Console.WriteLine();
    Console.WriteLine("CI: set FOUNDRY_ENDPOINT / FOUNDRY_DEPLOYMENT_NAME / FOUNDRY_TENANT_ID / FOUNDRY_CLIENT_ID / FOUNDRY_CLIENT_SECRET as secret variables.");
    return 1;
}

if (string.IsNullOrWhiteSpace(config.FoundryEndpoint))
{
    Console.WriteLine("FoundryEndpoint is required.");
    return 1;
}

var deploymentName = string.IsNullOrWhiteSpace(config.DeploymentName) ? "gpt-4o" : config.DeploymentName;
var usingSp = !string.IsNullOrWhiteSpace(config.TenantId) && !string.IsNullOrWhiteSpace(config.ClientId) && !string.IsNullOrWhiteSpace(config.ClientSecret);

Console.WriteLine("=== Foundry Connectivity Check ===");
Console.WriteLine($"Endpoint:    {config.FoundryEndpoint}");
Console.WriteLine($"Deployment:  {deploymentName}");
Console.WriteLine($"Auth:        {(usingSp ? $"Service Principal (tenant {Mask(config.TenantId)}, client {Mask(config.ClientId)})" : "Managed Identity (DefaultAzureCredential)")}");
Console.WriteLine();

try
{
    Console.WriteLine("Building agent via FoundryClientFactory.BuildAgent (same path as TestConnectionAsync)...");
    var agent = FoundryClientFactory.BuildAgent(
        config.FoundryEndpoint, deploymentName,
        "You are a connectivity check. Reply with a single word.",
        config.TenantId, config.ClientId, config.ClientSecret);

    Console.WriteLine("Calling agent.RunAsync(\"ping\")...");
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var response = await agent.RunAsync("ping");
    sw.Stop();

    Console.WriteLine();
    Console.WriteLine("SUCCESS");
    Console.WriteLine($"   Response ({sw.ElapsedMilliseconds}ms): {response.Text}");
    Console.WriteLine($"   Tokens used: {response.Usage?.TotalTokenCount ?? 0}");
    return 0;
}
catch (ClientResultException ex)
{
    Console.WriteLine();
    Console.WriteLine("FAILED — System.ClientModel.ClientResultException");
    Console.WriteLine($"   Status:  {ex.Status}");
    Console.WriteLine($"   Message: {Truncate(ex.Message, 400)}");

    var raw = ex.GetRawResponse();
    if (raw is not null)
    {
        raw.BufferContent();
        var body = raw.Content?.ToString();
        Console.WriteLine($"   Raw body: {(string.IsNullOrWhiteSpace(body) ? "(empty)" : Truncate(body, 1000))}");
    }
    return 1;
}
catch (Exception ex)
{
    Console.WriteLine();
    Console.WriteLine("FAILED — UNEXPECTED EXCEPTION TYPE (not ClientResultException — production catch clauses would NOT match this; investigate before checkin)");
    Console.WriteLine($"   Exception type: {ex.GetType().FullName}");
    Console.WriteLine($"   Message:        {Truncate(ex.Message, 400)}");
    return 2;
}

static string Mask(string? value)
    => string.IsNullOrEmpty(value) ? "(none)" : value.Length <= 8 ? "…" : $"{value[..8]}…";

static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

internal record TestConfig(
    string FoundryEndpoint,
    string? DeploymentName,
    string? TenantId,
    string? ClientId,
    string? ClientSecret);
