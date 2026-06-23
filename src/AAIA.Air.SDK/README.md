# AAIA.Air.SDK

Öffentliche Entwickleroberfläche für Tool-Provider der AAIA Intelligence Runtime.
Das SDK referenziert ausschließlich `AAIA.Air.Contracts` und kennt weder Runtime,
MCP noch eine konkrete AAIA-Anwendung.

```csharp
public sealed class FritzTools : AirToolProviderBase
{
    public override string ProviderId => "fritzbox";

    protected override void Configure(AirToolBuilder tools)
    {
        tools.Add(
            "fritz.status",
            "FRITZ!Box-Status",
            AiRiskLevel.Green,
            StatusAsync);
    }

    private static Task<AiToolResult> StatusAsync(
        AiToolInvocation invocation,
        CancellationToken cancellationToken)
        => Task.FromResult(AiToolResult.Ok(new { online = true }));
}
```

Der Provider kann anschließend über `runtime.Tools.RegisterProvider(new FritzTools())`
registriert werden. Sicherheitsprüfung, Permissions, Locks und Audit bleiben Aufgabe der AIR.
