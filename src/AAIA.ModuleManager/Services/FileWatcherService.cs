using System;
using System.IO;

namespace AAIA.ModuleManager.Services;

/// <summary>
/// Wraps a FileSystemWatcher on a project directory.
/// Raises Changed when any source file is modified.
/// </summary>
public sealed class FileWatcherService : IDisposable
{
    private FileSystemWatcher? _watcher;
    private string? _watchedPath;

    /// <summary>Fired when a .cs / .csproj / .json file in the project changes.</summary>
    public event EventHandler<string>? Changed;

    public string? WatchedPath => _watchedPath;
    public bool    IsWatching  => _watcher is { EnableRaisingEvents: true };

    public void Watch(string projectDir)
    {
        Stop();
        if (!Directory.Exists(projectDir)) return;

        _watchedPath = projectDir;
        _watcher = new FileSystemWatcher(projectDir)
        {
            IncludeSubdirectories  = true,
            NotifyFilter           = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            EnableRaisingEvents    = true
        };

        _watcher.Changed += OnFsEvent;
        _watcher.Created += OnFsEvent;
        _watcher.Renamed += (_, e) => OnChanged(e.FullPath);
    }

    private void OnFsEvent(object sender, FileSystemEventArgs e) => OnChanged(e.FullPath);

    private void OnChanged(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".cs" or ".csproj" or ".json" or ".axaml")
            Changed?.Invoke(this, path);
    }

    public void Stop()
    {
        if (_watcher is null) return;
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _watcher    = null;
        _watchedPath = null;
    }

    public void Dispose() => Stop();
}
