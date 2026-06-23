using System.Text.RegularExpressions;

namespace AAIA.Air.Messaging;

/// <summary>Minimale, adapterunabhängige Secret-Sperre für AIR-Nachrichten.</summary>
public static class AiMessageSafetyPolicy
{
    private static readonly Regex[] SensitivePatterns =
    {
        new(@"-----BEGIN\s+(RSA\s+|EC\s+|OPENSSH\s+)?PRIVATE KEY-----",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bBearer\s+[A-Za-z0-9._~+/=-]{20,}",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\b",
            RegexOptions.Compiled),
        new("""(?i)(api[_-]?key|access[_-]?token|client[_-]?secret|private[_-]?key)\s*[:=]\s*['"]?[A-Za-z0-9._~+/=-]{8,}""",
            RegexOptions.Compiled)
    };

    public static bool ContainsSensitiveContent(string? value)
        => !string.IsNullOrEmpty(value) && SensitivePatterns.Any(pattern => pattern.IsMatch(value));
}
