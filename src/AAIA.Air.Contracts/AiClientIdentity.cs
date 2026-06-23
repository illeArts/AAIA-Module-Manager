namespace AAIA.Air.Contracts;

/// <summary>
/// Identität eines verbundenen KI-Clients — Grundlage für Audit und Per-Client-Permissions.
/// Nicht nur das Bridge-Token: damit das Audit lesen kann "Claude 4.2 hat Build gestartet".
/// </summary>
public sealed class AiClientIdentity
{
    /// <summary>Anzeigename, z. B. "Claude Desktop".</summary>
    public required string Name { get; init; }

    /// <summary>Client-/Modellversion, z. B. "4.2".</summary>
    public string Version { get; init; } = "";

    /// <summary>Modellbezeichnung, z. B. "claude-opus-4-8".</summary>
    public string Model { get; init; } = "";

    /// <summary>Anbieter, z. B. "Anthropic", "OpenAI", "Google".</summary>
    public string Vendor { get; init; } = "";

    /// <summary>
    /// Stabiler Fingerprint des Clients (z. B. aus Client-Info beim Connect abgeleitet).
    /// Erlaubt es, Permissions an einen wiederkehrenden Client zu binden.
    /// </summary>
    public string Fingerprint { get; init; } = "";

    public override string ToString()
        => string.IsNullOrEmpty(Version) ? Name : $"{Name} {Version}";
}
