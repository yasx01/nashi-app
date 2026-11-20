using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Headless;

namespace Nashi.Linux;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--self-test")
        {
            AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            SelfTest.Run(args[1]);
            return 0;
        }
        return BuildApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildApp() => AppBuilder.Configure<App>().UsePlatformDetect();
}

public sealed class App : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var args = desktop.Args ?? [];
            if (args.Length == 2 && args[0] == "--desktop-smoke-test")
            {
                desktop.MainWindow = new MainWindow(dataDirectory: Path.Combine(Path.GetTempPath(), "nashi-desktop-test-" + Guid.NewGuid().ToString("N")));
                SelfTest.CheckDesktop(desktop, args[1]);
            }
            else desktop.MainWindow = new MainWindow(args);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
