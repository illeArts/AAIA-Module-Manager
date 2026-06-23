using AAIA.Air.Contracts;

namespace AAIA.Air.SDK;

/// <summary>
/// Basis für AIR-Tool-Provider. Module konfigurieren ihre Tools über den Builder und
/// müssen das interne Erzeugungsschema der Tool-Definitionen nicht selbst verwalten.
/// </summary>
public abstract class AirToolProviderBase : IAiToolProvider
{
    public abstract string ProviderId { get; }

    protected abstract void Configure(AirToolBuilder tools);

    public IEnumerable<AiToolDefinition> GetTools()
    {
        var builder = new AirToolBuilder();
        Configure(builder);
        return builder.Build();
    }
}
