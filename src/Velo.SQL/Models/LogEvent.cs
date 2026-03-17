namespace Velo.SQL.Models;

/// <summary>
/// Serilog structured log entity - persists all application logs to database for audit trail.
/// Includes security context (OrgId, UserId), request correlation, and performance metrics.
/// </summary>
public class LogEvent
{
    public int Id { get; set; }
    public string MessageTemplate { get; set; } = string.Empty;
    public string Level { get; set; } = string.Empty; // Debug, Information, Warning, Error, Fatal
    public DateTimeOffset TimeStamp { get; set; } = DateTimeOffset.UtcNow;
    public string? Exception { get; set; }
    public string EventData { get; set; } = string.Empty; // Full JSON

    // Security & Audit Context
    public string? OrgId { get; set; }
    public string? UserId { get; set; }
    public string? CorrelationId { get; set; }
    
    // Request Context
    public string? RequestPath { get; set; }
    public string? RequestMethod { get; set; }
    public int? StatusCode { get; set; }
    public int? DurationMs { get; set; }
    
    // Structured Properties
    public string? Properties { get; set; } // JSON
}
