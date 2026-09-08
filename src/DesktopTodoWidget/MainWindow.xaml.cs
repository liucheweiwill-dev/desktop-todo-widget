using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Input;
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
    private IntPtr _lastAttachedWorkerwParent;
    private HwndSource? _windowSource;
    private HwndSourceHook? _bottommostZOrderHook;
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
        Closed += MainWindow_Closed;

        // Spike 的視窗無邊框、不在工作列也不在 Alt+Tab，沒有任何一般的關閉途徑。
        // Esc 是給人工驗證用的關閉方式；用 Preview 以便 TextBox 有焦點時仍然有效。
        PreviewKeyDown += MainWindow_PreviewKeyDown;

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

        RemoveBottommostZOrderHook();

        if (!DesktopAttach.TryAttachToParent(_windowHandle, parent, WriteLog, out var attachFailure))
        {
            WorkerwFailed(attachFailure);
            return;
        }

        _workerwParent = parent;
        _lastAttachedWorkerwParent = parent;
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
        DesktopAttach.RequestDesktopRepaint(_lastAttachedWorkerwParent, WriteLog);
        _lastAttachedWorkerwParent = IntPtr.Zero;
        RunAsBottommost();

        StatusText.Text = string.Format(
            CultureInfo.InvariantCulture,
            Ui("WorkerwFailedStatusText"),
            reason);
    }

    private void RunAsBottommost()
    {
        InstallBottommostZOrderHook();

        var result = DesktopAttach.MoveToBottommost(_windowHandle);
        WriteLog($"bottommost success={result.Success} lastError={result.LastError} detail={result.Detail}");

        if (_options.Mode == AttachMode.Bottommost)
        {
            StatusText.Text = Ui("BottommostStatusText");
        }
    }

    private void InstallBottommostZOrderHook()
    {
        if (_bottommostZOrderHook is not null)
        {
            return;
        }

        var windowSource = HwndSource.FromHwnd(_windowHandle);
        if (windowSource is null)
        {
            WriteLog("bottommost z-order hook attach failed: HwndSource unavailable");
            return;
        }

        _windowSource = windowSource;
        _bottommostZOrderHook = BottommostZOrderHook;
        _windowSource.AddHook(_bottommostZOrderHook);
        WriteLog("bottommost z-order hook attached");
    }

    private void RemoveBottommostZOrderHook()
    {
        if (_bottommostZOrderHook is null)
        {
            return;
        }

        _windowSource?.RemoveHook(_bottommostZOrderHook);
        _bottommostZOrderHook = null;
        _windowSource = null;
        WriteLog("bottommost z-order hook removed");
    }

    private IntPtr BottommostZOrderHook(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message == DesktopAttach.WmWindowPosChanging && lParam != IntPtr.Zero)
        {
            var windowPos = Marshal.PtrToStructure<DesktopAttach.WindowPos>(lParam);
            windowPos.HwndInsertAfter = DesktopAttach.HwndBottom;
            windowPos.Flags &= ~DesktopAttach.SwpNoZOrder;
            Marshal.StructureToPtr(windowPos, lParam, fDeleteOld: false);
        }

        return IntPtr.Zero;
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        WriteLog("closing: Escape pressed");
        e.Handled = true;
        Close();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        RemoveBottommostZOrderHook();

        if (_lastAttachedWorkerwParent == IntPtr.Zero)
        {
            return;
        }

        var formerWorkerwParent = _lastAttachedWorkerwParent;
        _workerwParent = IntPtr.Zero;
        var restoreResult = DesktopAttach.RestoreTopLevelStyle(_windowHandle);
        WriteLog(
            $"close restore top-level success={restoreResult.Success} lastError={restoreResult.LastError} " +
            $"detail={restoreResult.Detail}");
        DesktopAttach.RequestDesktopRepaint(formerWorkerwParent, WriteLog);
        _lastAttachedWorkerwParent = IntPtr.Zero;
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
