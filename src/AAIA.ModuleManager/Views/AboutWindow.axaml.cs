using System.Reflection;
using Avalonia.Controls;

namespace AAIA.ModuleManager.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        VersionLabel.Text = $"v{ver?.Major}.{ver?.Minor}.{ver?.Build}";
    }
}
