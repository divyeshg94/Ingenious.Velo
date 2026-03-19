namespace Velo.SQL.Models;

/// <summary>
/// Serilog structured log entity — persists all application logs to the database for audit trail.
/// Column names are lowercase with underscores to match the Serilog MSSqlServer sink column options
/// configured in appsettings.json.
/// </summary>
public class LogEvent
{
    public int Id { get; set; }

    // Standard Serilog columns
    public string? Message { get; set; }                          // Rendered message
    public string MessageTemplate { get; set; } = string.Empty;
    public string Level { get; set; } = string.Empty;            // Information, Warning, Error, Fatal
    public DateTimeOffset TimeStamp { get; set; } = DateTimeOffset.UtcNow;
    public string? Exception { get; set; }
    public string? Properties { get; set; }                       // Enriched properties (JSON)
    public string? EventData { get; set; }                        // Full log-event JSON

    // Security & audit context (populated via LogContext.PushProperty in middleware)
    public string? OrgId { get; set; }
    public string? UserId { get; set; }
    public string? CorrelationId { get; set; }

    // Request context
    public string? RequestPath { get; set; }
    public string? RequestMethod { get; set; }
    public int? StatusCode { get; set; }
    public int? DurationMs { get; set; }
}

