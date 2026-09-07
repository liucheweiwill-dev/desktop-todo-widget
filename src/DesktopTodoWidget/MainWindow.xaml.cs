using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Windows;
using DesktopTodoWidget.Interop;

namespace DesktopTodoWidget;

public partial class MainWindow : Window
{
    private readonly StartupOptions _options;
    private readonly DispatcherTimer _parentCheckTimer;
    private readonly string _logPath;
    private IntPtr _windowHandle;
    private IntPtr _workerwParent;
    private int _clickCount;
    private int _reattachCount;

    internal MainWindow(StartupOptions options)
    {
        _options = options;
        _logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopTodoWidget",
            "spike.log");

        InitializeComponent();
        SourceInitialized += MainWindow_SourceInitialized;

        _parentCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _parentCheckTimer.Tick += ParentCheckTimer_Tick;
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        _windowHandle = new WindowInteropHelper(this).Handle;
        var dpi = DesktopAttach.GetDpiSnapshot(_windowHandle);
        WriteLog(
            $"startup mode={ModeName} target={TargetName} windowsBuild={Environment.OSVersion.Version.Build} " +
            $"hwnd={DesktopAttach.FormatHandle(_windowHandle)} dpiAwareness={dpi.Awareness} monitorDpi={dpi.Dpi}");

        if (_options.ParseWarning is not null)
        {
            WriteLog($"argument warning: {_options.ParseWarning}");
        }

        var toolWindowResult = DesktopAttach.ConfigureToolWindow(_windowHandle);
        WriteLog(
            $"tool-window style success={toolWindowResult.Success} lastError={toolWindowResult.LastError} " +
            $"detail={toolWindowResult.Detail}");

        if (_options.Mode == AttachMode.Workerw)
        {
            AttachWorkerw(isReattach: false);
        }
        else
        {
            RunAsBottommost();
        }
    }

    private void AttachWorkerw(bool isReattach)
    {
        if (!DesktopAttach.TryResolveDesktopParent(
                _options.Target == WorkerwTarget.Workerw,
                WriteLog,
                out var parent,
                out var resolutionFailure))
        {
            WorkerwFailed(resolutionFailure);
            return;
        }

        var parentClassName = DesktopAttach.GetWindowClassName(parent);
        WriteLog(
            $"selected parent hwnd={DesktopAttach.FormatHandle(parent)} class={parentClassName} " +
            $"target={TargetName}");

        if (!DesktopAttach.TryAttachToParent(_windowHandle, parent, WriteLog, out var attachFailure))
        {
            WorkerwFailed(attachFailure);
            return;
        }

        _workerwParent = parent;
        StatusText.Text = string.Format(
            CultureInfo.InvariantCulture,
            Ui("WorkerwAttachedStatusText"),
            TargetName);
        WriteLog($"workerw attach complete reattach={isReattach} parent={DesktopAttach.FormatHandle(parent)}");

        if (!_parentCheckTimer.IsEnabled)
        {
            _parentCheckTimer.Start();
        }
    }

    private void ParentCheckTimer_Tick(object? sender, EventArgs e)
    {
        if (_workerwParent == IntPtr.Zero || DesktopAttach.IsWindow(_workerwParent))
        {
            return;
        }

        _reattachCount++;
        WriteLog(
            $"workerw parent lost hwnd={DesktopAttach.FormatHandle(_workerwParent)} " +
            $"reattachAttempt={_reattachCount}");
        _workerwParent = IntPtr.Zero;
        AttachWorkerw(isReattach: true);
    }

    private void WorkerwFailed(string reason)
    {
        WriteLog($"workerw failed reason={reason}; falling back to bottommost");
        _workerwParent = IntPtr.Zero;
        _parentCheckTimer.Stop();

        var restoreResult = DesktopAttach.RestoreTopLevelStyle(_windowHandle);
        WriteLog(
            $"restore top-level success={restoreResult.Success} lastError={restoreResult.LastError} " +
            $"detail={restoreResult.Detail}");
        RunAsBottommost();

        StatusText.Text = string.Format(
            CultureInfo.InvariantCulture,
            Ui("WorkerwFailedStatusText"),
            reason);
    }

    private void RunAsBottommost()
    {
        var result = DesktopAttach.MoveToBottommost(_windowHandle);
        WriteLog($"bottommost success={result.Success} lastError={result.LastError} detail={result.Detail}");

        if (_options.Mode == AttachMode.Bottommost)
        {
            StatusText.Text = Ui("BottommostStatusText");
        }
    }

    private void VerifyButton_Click(object sender, RoutedEventArgs e)
    {
        _clickCount++;
        VerifyButton.Content = string.Format(CultureInfo.InvariantCulture, Ui("ClickedButtonText"), _clickCount);
    }

    private string ModeName => _options.Mode == AttachMode.Workerw ? "workerw" : "bottommost";

    private string TargetName => _options.Target == WorkerwTarget.Workerw ? "workerw" : "progman";

    private static string Ui(string key) => (string)Application.Current.Resources[key];

    private void WriteLog(string message)
    {
        var line = $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}";

        try
        {
            var directory = Path.GetDirectoryName(_logPath)!;
            Directory.CreateDirectory(directory);
            File.AppendAllText(_logPath, line, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Unable to write spike log: {exception.Message}");
        }
    }
}
