using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace RandomWalk.Correlation;

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
            var sources = args.Where(a => a.Contains(':')).ToList();
            if (sources.Count == 0) sources.Add($"localhost:{RandomWalk.Common.Ports.GeneratorPub}");
            desktop.MainWindow = new MainWindow(sources);
        }

        base.OnFrameworkInitializationCompleted();
    }
}