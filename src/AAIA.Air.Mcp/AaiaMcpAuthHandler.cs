using System;
using System.Security.Cryptography;

namespace AAIA.Air.Mcp;

/// <summary>
/// Bridge-Token-Handshake. localhost-only reicht nicht — jeder lokale Prozess könnte
/// sonst die Bridge ansprechen. Beim Start wird ein kryptografisch zufälliges Token
/// erzeugt und auf JEDEM Request geprüft. Das Token ist der Bridge-Zugangsschlüssel,
/// KEIN Projekt-Secret. Es ist rotierbar; Rotation invalidiert das alte Token.
/// </summary>
public sealed class AaiaMcpAuthHandler
{
    private string _token;
    private readonly object _gate = new();

    public AaiaMcpAuthHandler()
    {
        _token = Generate();
    }

    /// <summary>Aktuell gültiges Token (für die Client-Config).</summary>
    public string CurrentToken
    {
        get { lock (_gate) return _token; }
    }

    /// <summary>Erzeugt ein neues Token; das alte wird damit ungültig.</summary>
    public string Rotate()
    {
        lock (_gate)
        {
            _token = Generate();
            return _token;
        }
    }

    /// <summary>Prüft das vom Client gesendete Token konstantzeitlich.</summary>
    public bool Validate(string? presentedToken)
    {
        if (string.IsNullOrEmpty(presentedToken)) return false;
        lock (_gate)
        {
            var a = System.Text.Encoding.UTF8.GetBytes(presentedToken);
            var b = System.Text.Encoding.UTF8.GetBytes(_token);
            return CryptographicOperations.FixedTimeEquals(a, b);
        }
    }

    /// <summary>Extrahiert das Token aus einem "Authorization: Bearer &lt;token&gt;"-Header.</summary>
    public bool ValidateAuthorizationHeader(string? authorizationHeader)
    {
        if (string.IsNullOrEmpty(authorizationHeader)) return false;
        const string prefix = "Bearer ";
        var value = authorizationHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? authorizationHeader[prefix.Length..].Trim()
            : authorizationHeader.Trim();
        return Validate(value);
    }

    private static string Generate()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
