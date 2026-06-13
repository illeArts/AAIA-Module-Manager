using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace AAIA.ModuleManager.ViewModels;

/// <summary>Konvertiert LoginWindowViewModel.Screen → bool für IsVisible-Bindings.</summary>
public sealed class ScreenConverter : IValueConverter
{
    public static readonly ScreenConverter IsLogin    = new(LoginWindowViewModel.Screen.Login);
    public static readonly ScreenConverter IsRegister = new(LoginWindowViewModel.Screen.Register);
    public static readonly ScreenConverter IsTotpSetup= new(LoginWindowViewModel.Screen.TotpSetup);

    private readonly LoginWindowViewModel.Screen _target;
    private ScreenConverter(LoginWindowViewModel.Screen target) => _target = target;

    public object Convert(object? value, Type t, object? p, CultureInfo c)
        => value is LoginWindowViewModel.Screen s && s == _target;

    public object ConvertBack(object? v, Type t, object? p, CultureInfo c)
        => throw new NotSupportedException();
}

/// <summary>bool IsError → Foreground-Farbe (rot / gedimmt weiß).</summary>
public sealed class BoolToColorConverter : IValueConverter
{
    public static readonly BoolToColorConverter Instance = new();

    public object Convert(object? value, Type t, object? p, CultureInfo c)
        => value is true
            ? new SolidColorBrush(Color.Parse("#e74c3c"))
            : new SolidColorBrush(Color.Parse("#8892a4"));

    public object ConvertBack(object? v, Type t, object? p, CultureInfo c)
        => throw new NotSupportedException();
}

/// <summary>bool ShowPassword → PasswordChar: true = '\0' (sichtbar), false = '●' (verdeckt).</summary>
public sealed class PasswordVisibilityConverter : IValueConverter
{
    public static readonly PasswordVisibilityConverter Instance = new();

    public object Convert(object? value, Type t, object? p, CultureInfo c)
        => value is true ? '\0' : '●';

    public object ConvertBack(object? v, Type t, object? p, CultureInfo c)
        => throw new NotSupportedException();
}

/// <summary>bool ShowPassword → Auge-Icon: true = Auge durchgestrichen (verbergen), false = Auge (anzeigen).</summary>
public sealed class EyeIconConverter : IValueConverter
{
    public static readonly EyeIconConverter Instance = new();

    public object Convert(object? value, Type t, object? p, CultureInfo c)
        => value is true ? "🙈" : "👁";

    public object ConvertBack(object? v, Type t, object? p, CultureInfo c)
        => throw new NotSupportedException();
}
