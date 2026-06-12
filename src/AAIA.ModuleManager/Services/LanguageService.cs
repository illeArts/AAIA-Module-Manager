using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AAIA.ModuleManager.Services;

public enum AppLanguage { De, En }

/// <summary>
/// Swaps the active string ResourceDictionary at runtime.
/// All DynamicResource bindings update immediately.
/// </summary>
public static class LanguageService
{
    private const string DeUri = "avares://AAIA.ModuleManager/Assets/Strings.de.axaml";
    private const string EnUri = "avares://AAIA.ModuleManager/Assets/Strings.en.axaml";

    public static AppLanguage Current { get; private set; } = AppLanguage.De;

    public static void SetLanguage(AppLanguage lang)
    {
        Current = lang;

        var app = Application.Current;
        if (app is null) return;

        // Application.Resources.MergedDictionaries only ever contains our
        // language dicts — safe to clear and re-add.
        app.Resources.MergedDictionaries.Clear();

        var uri = new Uri(lang == AppLanguage.En ? EnUri : DeUri);
        var dict = (IResourceDictionary)AvaloniaXamlLoader.Load(uri);
        app.Resources.MergedDictionaries.Add(dict);
    }

    /// <summary>Call after loading AppConfig to apply the stored language preference.</summary>
    public static void ApplyFromConfig(string langCode)
    {
        var lang = langCode?.ToLowerInvariant() == "en" ? AppLanguage.En : AppLanguage.De;
        SetLanguage(lang);
    }
}
