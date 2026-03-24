using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace RandomWalk.Display;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var args = desktop.Args ?? [];
            var source = args.Length >= 1 && args[0].Contains(':') ? args[0] : $"localhost:{RandomWalk.Common.Ports.GeneratorPub}";
            desktop.MainWindow = new MainWindow(source);
        }

        base.OnFrameworkInitializationCompleted();
    }
}