using AAIA.Air.Mcp;
using AAIA.Air.Hosts;

namespace AAIA.Air;

/// <summary>
/// Setzt den kompletten AI-Runtime-Stack zusammen (Runtime-Kern + MCP-Adapter + Server).
/// Eine Zeile in der App genügt, um die Bridge betriebsbereit zu machen.
/// </summary>
public sealed class AiRuntimeComposition
{
    public AiRuntimeService Runtime { get; }
    public AaiaMcpAuthHandler Auth { get; }
    public AaiaMcpAdapter Adapter { get; }
    public AaiaMcpServer Server { get; }
    public AaiaMcpConfigService Config { get; }
    public AaiaMcpBridgeOptions Options { get; }

    /// <param name="hosts">
    /// Von der App bereitgestellte Hosts (Status, Build, Validate, …). Die Runtime kennt
    /// nur die Interfaces. Increment 1 liefert i. d. R. nur den Status-Host; die übrigen
    /// folgen in Increment 2. Nicht registrierte Hosts → Tool liefert "host_unavailable".
    /// </param>
    public AiRuntimeComposition(AaiaMcpBridgeOptions options, params IAiHost[] hosts)
    {
        Options = options;

        Runtime = new AiRuntimeService(
            new AiToolRegistry(),
            new AiSessionManager(),
            new AiCapabilityManager(),
            new AiPermissionEngine(),
            new AiWorkspaceLockService(),
            new AiRuntimeEventBus(),
            new AiAuditService());

        // Von der App bereitgestellte Hosts registrieren (über ihr jeweiliges Interface).
        foreach (var h in hosts) RegisterHost(h);

        // Default-Permissions neuer Sessions aus den UI-Toggles ableiten.
        Auth    = new AaiaMcpAuthHandler();
        Adapter = new AaiaMcpAdapter(Runtime, Runtime.Sessions, Runtime.Capabilities, options);
        Runtime.Permissions.DefaultPermissions = Adapter.DefaultPermissionsFromOptions();

        Server  = new AaiaMcpServer(options, Auth, Adapter);
        Config  = new AaiaMcpConfigService(options, Auth);

        // Kern-Tools + Runtime-Tools + Task-/Collaboration-Tools registrieren.
        AaiaCoreToolsBootstrap.RegisterAll(Runtime);
    }

    /// <summary>
    /// Übernimmt normalisierte Profile und Telemetrie aus dem registrierten Host.
    /// Die Methode verändert weder Permissions noch Scheduler-Zustand.
    /// </summary>
    public int RefreshResourceHost()
    {
        var host = Runtime.Hosts.Get<IAiResourceHost>();
        if (host is null) return 0;

        var changed = 0;
        foreach (var profile in host.GetResourceProfiles())
        {
            if (!string.Equals(profile.ProviderId, host.HostId, StringComparison.Ordinal))
                throw new InvalidOperationException("Resource ProviderId muss der HostId entsprechen.");
            Runtime.Resources.Registry.RegisterOrUpdate(profile);
            changed++;
        }
        foreach (var telemetry in host.GetResourceTelemetry())
        {
            Runtime.Resources.Registry.UpdateTelemetry(telemetry);
            changed++;
        }
        return changed;
    }

    /// <summary>Registriert einen Host unter ALLEN Host-Interfaces, die er implementiert.</summary>
    private void RegisterHost(IAiHost host)
    {
        if (host is IAiStatusHost s)      Runtime.Hosts.Register(s);
        if (host is IAiResourceHost r)    Runtime.Hosts.Register(r);
        if (host is IAiProjectHost p)     Runtime.Hosts.Register(p);
        if (host is IAiFileHost f)        Runtime.Hosts.Register(f);
        if (host is IAiPatchHost pa)      Runtime.Hosts.Register(pa);
        if (host is IAiValidationHost v)  Runtime.Hosts.Register(v);
        if (host is IAiBuildHost b)       Runtime.Hosts.Register(b);
        if (host is IAiPackageHost pk)    Runtime.Hosts.Register(pk);
        if (host is IAiIdeHost i)         Runtime.Hosts.Register(i);
        if (host is IAiTerminalHost t)    Runtime.Hosts.Register(t);
        if (host is IAiMarketplaceHost m) Runtime.Hosts.Register(m);
    }
}
