using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using System.Text.Json;

namespace StartupStudio;
public static class Program
{
    [STAThread] public static void Main(string[] args)
    {
        using var mutex = new Mutex(true, "HasselbladStartupStudio-Windows", out var acquired);
        if (!acquired) return;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<StudioApplication>().UsePlatformDetect();
}
public sealed class StudioApplication : Application
{
    public override void Initialize() { RequestedThemeVariant = ThemeVariant.Light; Styles.Add(new FluentTheme()); }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new StudioWindow(); desktop.MainWindow = window;
            if (desktop.Args?.Contains("--smoke-ui") == true)
            {
                window.LinkFactory = (_, _) => throw new StudioException("离线启动检查禁止 USB 访问。");
                window.SetImage(new GrayImage(321, 239, Enumerable.Range(0, 321 * 239).Select(i => (byte)(i % 251)).ToArray()), "离线测试图");
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                timer.Tick += (_, _) =>
                {
                    timer.Stop(); var folder = Path.Combine(AppContext.BaseDirectory, "smoke-results"); Directory.CreateDirectory(folder);
                    try
                    {
                        Check.That(OperatingSystem.IsWindows() && window.IsVisible && window.Bounds.Width > 1000 && !window.Install.IsEnabled && !window.Restore.IsEnabled, "Windows 原生窗口启动检查失败。");
                        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height)); bitmap.Render(window);
                        bitmap.Save(Path.Combine(folder, "native-windows-ui.png"), PngBitmapEncoderOptions.Default);
                        File.WriteAllText(Path.Combine(folder, "result.json"), JsonSerializer.Serialize(new { passed = true, windowsNativeUi = true, deviceAccessed = false, driverTested = false })); desktop.Shutdown(0);
                    }
                    catch (Exception e) { File.WriteAllText(Path.Combine(folder, "failure.txt"), e.Message); desktop.Shutdown(1); }
                };
                timer.Start();
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}
