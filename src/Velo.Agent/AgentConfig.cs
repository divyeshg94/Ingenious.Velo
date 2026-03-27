namespace Velo.Agent;

public class AgentConfig
{
    public string FoundryEndpoint { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;

    /// <summary>
    /// Plaintext API key (decrypted by AgentConfigService before passing here).
    /// When set, VeloAgent uses AzureKeyCredential instead of DefaultAzureCredential.
    /// </summary>
    public string? ApiKey { get; set; }
    public string DeploymentName { get; set; } = "gpt-4o";
    public int DailyTokenBudgetPerOrg { get; set; } = 50_000;

    /// <summary>
    /// Cache TTL for Foundry responses keyed by pipeline definition hash.
    /// Eliminates repeat token spend for unchanged pipelines.
    /// </summary>
    public TimeSpan ResponseCacheTtl { get; set; } = TimeSpan.FromHours(6);
}
