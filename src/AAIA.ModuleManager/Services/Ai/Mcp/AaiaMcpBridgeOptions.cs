namespace AAIA.ModuleManager.Services.Ai.Mcp;

/// <summary>Transportmodus der MCP-Bridge.</summary>
public enum AaiaMcpTransportMode
{
    /// <summary>Phase 7.0 — Standard, mehrclientfähig (ein Server, viele Clients).</summary>
    StreamableHttp,

    /// <summary>Optionaler Einzelclient-Wrapper, später.</summary>
    Stdio
}

/// <summary>
/// Persistierte Einstellungen der MCP-Bridge. Teil von AppConfig.
/// Standardmäßig deaktiviert, nur localhost, eigener Port (getrennt vom Connector 39157).
/// </summary>
public sealed class AaiaMcpBridgeOptions
{
    /// <summary>Bridge aktiviert? Default AUS — muss bewusst eingeschaltet werden.</summary>
    public bool Enabled { get; set; } = false;

    public bool AutoStart { get; set; } = false;

    /// <summary>Standard-Port der MCP-Bridge (getrennt vom Connector-Server).</summary>
    public int Port { get; set; } = 39158;

    public AaiaMcpTransportMode Transport { get; set; } = AaiaMcpTransportMode.StreamableHttp;

    /// <summary>HTTP-Pfad des MCP-Endpunkts.</summary>
    public string Path { get; set; } = "/mcp";

    // ── Default-Permissions neuer Sessions (UI-Toggles) ─────────────────────────
    public bool AllowFileChanges { get; set; } = false;
    public bool AllowBuild       { get; set; } = false;
    public bool AllowTerminal    { get; set; } = false;
    public bool AllowOpenIde     { get; set; } = false;

    /// <summary>Signatur/Marketplace — in 7.0 nur vorbereitet, immer AUS.</summary>
    public bool AllowSignMarketplace { get; set; } = false;
}
