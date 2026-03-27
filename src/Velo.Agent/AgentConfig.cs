namespace Velo.Agent;

public class AgentConfig
{
    public string OrgId { get; set; } = string.Empty;
    public string FoundryEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// Foundry agent ID (e.g. asst_xxxx). Nullable — when null VeloAgent auto-creates
    /// the agent on first use and persists the returned ID via IAgentDataProvider.
    /// </summary>
    public string? AgentId { get; set; }

    /// <summary>
    /// Authentication credentials for Foundry access. Priority order used by VeloAgent:
    ///   1. API key present                      → ApiKeyTokenCredential (simplest)
    ///   2. TenantId + ClientId + ClientSecret   → ClientSecretCredential (cross-tenant SP)
    ///   3. None                                 → DefaultAzureCredential (Velo Managed Identity)
    /// Values are decrypted by AgentConfigService before passing here — never stored in plaintext.
    /// </summary>
    public string? ApiKey { get; set; }

    public string? TenantId { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string DeploymentName { get; set; } = "gpt-4o";
    public int DailyTokenBudgetPerOrg { get; set; } = 50_000;

    /// <summary>
    /// Cache TTL for Foundry responses keyed by pipeline definition hash.
    /// Eliminates repeat token spend for unchanged pipelines.
    /// </summary>
    public TimeSpan ResponseCacheTtl { get; set; } = TimeSpan.FromHours(6);
}
