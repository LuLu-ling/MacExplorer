using System.Text.RegularExpressions;

namespace MacExplorer.Logging;

/// <summary>
/// 将异常信息格式化为面向用户的消息，同时不改变原始的失败原因。
/// </summary>
public static partial class ExceptionDetails
{
    private const int MaxSingleMessageLength = 4096;
    private const int MaxCombinedMessageLength = 8192;

    public static string GetUserReason(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var messages = new List<string>();
        var seenMessages = new HashSet<string>(StringComparer.Ordinal);
        var current = exception;
        for (var depth = 0; current is not null && depth < 32; depth++, current = current.InnerException)
        {
            var message = current.Message?.Trim();
            if (string.IsNullOrWhiteSpace(message) || string.Equals(message, "$$", StringComparison.Ordinal))
                continue;
            message = TruncateForUser(RedactSensitiveText(message));
            if (seenMessages.Add(message))
                messages.Add(message);
        }

        return TruncateForUser(string.Join(Environment.NewLine, messages), MaxCombinedMessageLength);
    }

    public static string GetDebugDetails(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception.ToString();
    }

    public static string Compose(string summary, Exception? exception = null) =>
        Compose(summary, exception is null ? null : GetDebugDetails(exception));

    public static string Compose(string summary, string? details)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        return string.IsNullOrWhiteSpace(details)
            ? summary
            : $"{summary.TrimEnd()}{Environment.NewLine}{Environment.NewLine}{details}";
    }

    private static string TruncateForUser(string text, int? maxLength = null)
    {
        var limit = maxLength ?? MaxSingleMessageLength;
        return text.Length <= limit ? text : text[..limit] + Environment.NewLine + "…";
    }

    [GeneratedRegex(
        """(\b(?:access[_-]?token|refresh[_-]?token|client[_-]?secret|password|authorization)\b\s*[:=]\s*)(?:"[^"]*"|\S+)""",
        RegexOptions.IgnoreCase)]
    private static partial Regex KeyValueCredentialPattern();

    [GeneratedRegex(@"([?&](?:access_token|refresh_token|token|password|client_secret)=)[^&\s]+", RegexOptions.IgnoreCase)]
    private static partial Regex QueryStringCredentialPattern();

    [GeneratedRegex(@"\bBearer\s+[A-Za-z0-9._~+/=-]+", RegexOptions.IgnoreCase)]
    private static partial Regex BearerTokenPattern();

    private static string RedactSensitiveText(string text)
    {
        text = KeyValueCredentialPattern().Replace(text, "$1[redacted]");
        text = QueryStringCredentialPattern().Replace(text, "$1[redacted]");
        return BearerTokenPattern().Replace(text, "Bearer [redacted]");
    }
}
