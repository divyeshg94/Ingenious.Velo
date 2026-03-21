namespace Velo.Shared.Models;

public class WebhookStatusDto
{
    public bool IsRegistered { get; set; }
    public string? SubscriptionId { get; set; }
    public string? WebhookUrl { get; set; }
    public string? ProjectId { get; set; }
    public string? CreatedDate { get; set; }
    public string? LastFailureMessage { get; set; }
    public string? RegistrationError { get; set; }
    public string? ManualSetupUrl { get; set; }
}
