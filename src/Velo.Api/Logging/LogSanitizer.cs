namespace Velo.Api.Logging;

internal static class LogSanitizer
{
    // Strips newlines/tabs and truncates to a safe length for logging.
    public static string SanitiseForLog(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "(empty)";
        var sanitised = System.Text.RegularExpressions.Regex.Replace(value, "[\r\n\t]", " ");
        return sanitised.Length > 500 ? sanitised.Substring(0, 500) + "..." : sanitised;
    }

    // Used for values that must not be exposed even as a preview.
    public static string RedactSensitivePreview() => "(redacted)";
}
