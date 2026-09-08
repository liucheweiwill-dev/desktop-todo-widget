using System.Diagnostics;
using System.IO;
using System.Windows;
using DesktopTodoWidget.Data;

namespace DesktopTodoWidget;

public partial class App : Application
{
    private SingleInstanceGuard? _singleInstanceGuard;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var guard = SingleInstanceGuard.TryAcquire("Main");
        if (guard is null)
        {
            Trace.WriteLine("DesktopTodoWidget startup skipped because another instance is already running.");
            Shutdown();
            return;
        }

        _singleInstanceGuard = guard;
        var dataDirectory = new AppPaths().Resolve().DataDirectory;
        var placementStore = new AtomicJsonStore<WindowPlacement>(Path.Combine(dataDirectory, "window.json"));
        var window = new MainWindow(placementStore);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstanceGuard?.Dispose();
        _singleInstanceGuard = null;
        base.OnExit(e);
    }
}
