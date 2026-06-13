using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using AAIA.ModuleManager.ViewModels;
using QRCoder;
using System.IO;

namespace AAIA.ModuleManager.Views;

public partial class LoginWindow : Window
{
    public LoginWindow()
    {
        InitializeComponent();
    }

    public LoginWindow(LoginWindowViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;

        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LoginWindowViewModel.TotpUri) &&
                !string.IsNullOrEmpty(vm.TotpUri))
            {
                RenderQrCode(vm.TotpUri);
            }
        };
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void Close_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Close();

    private void RenderQrCode(string content)
    {
        try
        {
            using var generator = new QRCodeGenerator();
            var data    = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);
            using var qr = new PngByteQRCode(data);
            var pngBytes = qr.GetGraphic(8, [0x7C, 0x8C, 0xF8], [0x0D, 0x0D, 0x1E]);
            var bmp = new Bitmap(new MemoryStream(pngBytes));

            if (this.FindControl<Image>("QrCodeImage") is { } img)
                img.Source = bmp;
        }
        catch
        {
            // Falls QRCoder nicht verfügbar → kein QR, Secret-Text reicht als Fallback
        }
    }
}
