namespace AAIA.Air.Contracts;

/// <summary>Bekannte externe Modul-Fähigkeiten.</summary>
public static class AiRequiredCapabilities
{
    public const string Filesystem = "filesystem";
    public const string Scanner = "scanner";
    public const string Router = "router";
    public const string Docker = "docker";
    public const string Git = "git";
    public const string Network = "network";
}

/// <summary>Deklariert die externen Fähigkeiten, die ein Modul benötigt.</summary>
public interface IAiCapabilityProvider
{
    string ProviderId { get; }
    IEnumerable<string> RequiredCapabilities();
}
