using System.Diagnostics;
using System.Text.RegularExpressions;
using Serilog.Context;

namespace Velo.Api.Middleware;

/// <summary>
/// Correlation ID middleware - generates or extracts a unique request ID for tracing.
/// This ID flows through all logs for a single request, enabling full request-to-response tracking.
///
/// SECURITY: The incoming X-Correlation-ID header value is sanitised before use to prevent
/// log injection attacks (e.g. crafted IDs containing newlines that could forge log entries).
/// </summary>
public class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    private const string CorrelationIdHeader = "X-Correlation-ID";
    private const int    MaxCorrelationIdLength = 64;
    // Allow alphanumeric + hyphens only — reject any character that could poison structured logs.
    private static readonly Regex SafeCorrelationId =
        new(@"^[a-zA-Z0-9\-]{1,64}$", RegexOptions.Compiled);

    public async Task InvokeAsync(HttpContext context)
    {
        // Extract correlation ID from header; validate and sanitise before trusting it.
        string correlationId;
        if (context.Request.Headers.TryGetValue(CorrelationIdHeader, out var headerValue))
        {
            var raw = headerValue.ToString();
            // Accept the client-supplied ID only if it matches the safe pattern.
            // Otherwise generate a fresh one — never log or echo back a malformed value.
            correlationId = SafeCorrelationId.IsMatch(raw)
                ? raw
                : $"{Guid.NewGuid():N}";
        }
        else
        {
            correlationId = $"{Guid.NewGuid():N}";
        }

        // Store on HttpContext for middleware access
        context.Items["CorrelationId"] = correlationId;

        // Add to response headers
        context.Response.Headers.Append(CorrelationIdHeader, correlationId);

        // Push correlation ID to Serilog context (will be included in all logs for this request)
        using (LogContext.PushProperty("CorrelationId", correlationId))
        using (LogContext.PushProperty("RequestPath", context.Request.Path))
        using (LogContext.PushProperty("RequestMethod", context.Request.Method))
        {
            var stopwatch = Stopwatch.StartNew();
            
            try
            {
                logger.LogInformation(
                    "HTTP {RequestMethod} {RequestPath} started - CorrelationId: {CorrelationId}",
                    context.Request.Method, context.Request.Path, correlationId);

                await next(context);

                stopwatch.Stop();
                logger.LogInformation(
                    "HTTP {RequestMethod} {RequestPath} completed with status {StatusCode} in {DurationMs}ms - CorrelationId: {CorrelationId}",
                    context.Request.Method, context.Request.Path, context.Response.StatusCode, stopwatch.ElapsedMilliseconds, correlationId);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                logger.LogError(ex,
                    "HTTP {RequestMethod} {RequestPath} failed after {DurationMs}ms - CorrelationId: {CorrelationId}",
                    context.Request.Method, context.Request.Path, stopwatch.ElapsedMilliseconds, correlationId);
                throw;
            }
        }
    }
}
