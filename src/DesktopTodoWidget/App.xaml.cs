using System.Windows;

namespace DesktopTodoWidget;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var options = StartupOptions.Parse(e.Args);
        var window = new MainWindow(options);
        MainWindow = window;
        window.Show();
    }
}

internal enum AttachMode
{
    Bottommost,
    Workerw
}

internal enum WorkerwTarget
{
    Progman,
    Workerw
}

internal sealed record StartupOptions(AttachMode Mode, WorkerwTarget Target, string? ParseWarning)
{
    public static StartupOptions Parse(IEnumerable<string> arguments)
    {
        var mode = AttachMode.Bottommost;
        var target = WorkerwTarget.Progman;
        string? warning = null;

        foreach (var argument in arguments)
        {
            if (argument.StartsWith("--mode=", StringComparison.OrdinalIgnoreCase))
            {
                var value = argument["--mode=".Length..];
                if (value.Equals("bottommost", StringComparison.OrdinalIgnoreCase))
                {
                    mode = AttachMode.Bottommost;
                }
                else if (value.Equals("workerw", StringComparison.OrdinalIgnoreCase))
                {
                    mode = AttachMode.Workerw;
                }
                else
                {
                    warning = $"Unknown mode '{value}'; using bottommost.";
                }
            }
            else if (argument.StartsWith("--target=", StringComparison.OrdinalIgnoreCase))
            {
                var value = argument["--target=".Length..];
                if (value.Equals("progman", StringComparison.OrdinalIgnoreCase))
                {
                    target = WorkerwTarget.Progman;
                }
                else if (value.Equals("workerw", StringComparison.OrdinalIgnoreCase))
                {
                    target = WorkerwTarget.Workerw;
                }
                else
                {
                    warning = $"Unknown target '{value}'; using progman.";
                }
            }
        }

        return new StartupOptions(mode, target, warning);
    }
}
