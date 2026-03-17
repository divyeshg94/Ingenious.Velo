using System.Diagnostics;
using Serilog.Context;

namespace Velo.Api.Middleware;

/// <summary>
/// Correlation ID middleware - generates or extracts a unique request ID for tracing.
/// This ID flows through all logs for a single request, enabling full request-to-response tracking.
/// 
/// SECURITY: Correlation IDs help detect suspicious patterns and trace security incidents.
/// </summary>
public class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    private const string CorrelationIdHeader = "X-Correlation-ID";

    public async Task InvokeAsync(HttpContext context)
    {
        // Extract correlation ID from header or generate new one
        var correlationId = context.Request.Headers.TryGetValue(CorrelationIdHeader, out var headerValue)
            ? headerValue.ToString()
            : $"{Guid.NewGuid():N}";

        // Store on HttpContext for middleware access
        context.Items["CorrelationId"] = correlationId;

        // Add to response headers
        context.Response.Headers.Add(CorrelationIdHeader, correlationId);

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
