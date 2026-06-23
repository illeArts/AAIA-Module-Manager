namespace AAIA.Air;

/// <summary>
/// Schlanker, secret-freier Statusausschnitt für aaia.status.get.
///
/// Hinweis: Der frühere Sammel-Host "IAaiaRuntimeHost" wurde in Increment 1.1 durch
/// fähigkeitsspezifische Host-Interfaces ersetzt (siehe Hosts/AiHostInterfaces.cs).
/// Status läuft jetzt über <see cref="Hosts.IAiStatusHost"/>; die Runtime kennt keine
/// konkrete App mehr, sondern nur Interfaces.
/// </summary>
public sealed class AaiaProjectStatus
{
    public string App { get; init; } = "AAIA Module Manager";
    public string Version { get; init; } = "";
    public bool ConnectorRunning { get; init; }
    public bool McpBridgeRunning { get; init; }
    public string? CurrentProject { get; init; }
    public string? PipelineStep { get; init; }
    public string TrustLevel { get; init; } = "Unsigned";
    public bool AaiasConnected { get; init; }
}
