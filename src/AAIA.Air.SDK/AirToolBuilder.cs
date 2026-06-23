using System.Text.Json;
using AAIA.Air.Contracts;

namespace AAIA.Air.SDK;

/// <summary>Fluent Builder für öffentliche AIR-Tool-Definitionen.</summary>
public sealed class AirToolBuilder
{
    private readonly List<AiToolDefinition> _tools = new();
    private static readonly JsonElement EmptyObjectSchema = CreateEmptyObjectSchema();

    public AirToolBuilder Add(
        string name,
        string description,
        AiRiskLevel risk,
        Func<AiToolInvocation, CancellationToken, Task<AiToolResult>> handler,
        AiPermission permissions = AiPermission.Read,
        JsonElement? inputSchema = null,
        bool requiresApproval = false,
        params string[] requiredCapabilities)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(handler);

        if (_tools.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"AIR-Tool '{name}' wurde bereits registriert.");

        _tools.Add(new AiToolDefinition
        {
            Name = name,
            Description = description,
            RiskLevel = risk,
            RequiredPermissions = permissions,
            InputSchema = inputSchema ?? EmptyObjectSchema,
            RequiresApproval = requiresApproval,
            RequiredCapabilities = requiredCapabilities ?? Array.Empty<string>(),
            Handler = handler
        });

        return this;
    }

    public IReadOnlyList<AiToolDefinition> Build() => _tools.ToArray();

    private static JsonElement CreateEmptyObjectSchema()
    {
        using var document = JsonDocument.Parse("""{"type":"object","properties":{}}""");
        return document.RootElement.Clone();
    }
}
