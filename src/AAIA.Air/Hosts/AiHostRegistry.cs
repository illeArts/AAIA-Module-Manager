using System;
using System.Collections.Concurrent;

namespace AAIA.Air.Hosts;

/// <summary>
/// Registry der von der App bereitgestellten Hosts. Die Runtime löst Hosts nur über
/// ihr Interface auf und weiß nie, welche konkrete App dahinter steckt.
/// </summary>
public sealed class AiHostRegistry
{
    private readonly ConcurrentDictionary<Type, IAiHost> _hosts = new();

    public void Register<T>(T host) where T : class, IAiHost
        => _hosts[typeof(T)] = host;

    public T? Get<T>() where T : class, IAiHost
        => _hosts.TryGetValue(typeof(T), out var h) ? (T)h : null;

    public bool Has<T>() where T : class, IAiHost
        => _hosts.ContainsKey(typeof(T));
}
