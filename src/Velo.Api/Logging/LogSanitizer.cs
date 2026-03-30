namespace Velo.Api.Logging;

internal static class LogSanitizer
{
    // Strips all ASCII C0 control characters (U+0000–U+001F) and DEL (U+007F),
    // then truncates to a safe max length.  Prevents log-injection attacks where
    // embedded CR/LF forge fake log lines in text sinks or corrupt JSON output.
    public static string SanitiseForLog(string? value, int maxLen = 500)
    {
        if (string.IsNullOrEmpty(value)) return "(empty)";
        var sanitised = System.Text.RegularExpressions.Regex.Replace(value, @"[\x00-\x1F\x7F]", " ");
        return sanitised.Length > maxLen ? sanitised[..maxLen] + "…" : sanitised;
    }

    // Used for values that must not be exposed even as a preview.
    public static string RedactSensitivePreview() => "(redacted)";
}
