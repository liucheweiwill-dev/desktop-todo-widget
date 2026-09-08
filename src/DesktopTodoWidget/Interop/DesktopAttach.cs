using System.Runtime.InteropServices;
using System.Text;

namespace DesktopTodoWidget.Interop;

internal static class DesktopAttach
{
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const nint WsChild = 0x40000000;
    // 不能用 const：nint 的大小依平台而定，(nint)0x80000000 不是編譯期常數（CS0133）。
    private static readonly nint WsPopup = unchecked((nint)0x80000000);
    private const nint WsExToolWindow = 0x00000080;
    private const nint WsExAppWindow = 0x00040000;
    internal const int WmWindowPosChanging = 0x0046;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    internal const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint RdwInvalidate = 0x0001;
    private const uint RdwErase = 0x0004;
    private const uint RdwAllChildren = 0x0080;
    private const uint RdwUpdateNow = 0x0100;
    private const uint ProgmanCreateWorkerwMessage = 0x052C;
    internal static readonly IntPtr HwndBottom = new(1);

    public static InteropOperationResult ConfigureToolWindow(IntPtr window)
    {
        var before = GetWindowLongPtr(window, GwlExStyle);
        var after = (before | WsExToolWindow) & ~WsExAppWindow;
        var setStyleResult = SetWindowLongPtrChecked(window, GwlExStyle, after);
        if (!setStyleResult.Success)
        {
            return new InteropOperationResult(false, setStyleResult.LastError, $"exstyle={FormatStyle(before)}");
        }

        if (!SetWindowPos(window, IntPtr.Zero, 0, 0, 0, 0, SwpFrameChanged | SwpNoMove | SwpNoSize | SwpNoZOrder))
        {
            return new InteropOperationResult(
                false,
                Marshal.GetLastPInvokeError(),
                $"exstyle before={FormatStyle(before)} after={FormatStyle(after)}");
        }

        return new InteropOperationResult(
            true,
            0,
            $"exstyle before={FormatStyle(before)} after={FormatStyle(after)}");
    }

    public static bool TryResolveDesktopParent(
        bool useWorkerw,
        Action<string> writeLog,
        out IntPtr parent,
        out string reason)
    {
        parent = FindWindow("Progman", null);
        writeLog($"workerw selection step 1: Progman hwnd={FormatHandle(parent)}");
        if (parent == IntPtr.Zero)
        {
            reason = "Progman not found";
            return false;
        }

        if (!useWorkerw)
        {
            writeLog("workerw selection: using Progman target");
            reason = string.Empty;
            return true;
        }

        writeLog("workerw selection step 2: sending 0x052C to Progman");
        _ = SendMessage(parent, ProgmanCreateWorkerwMessage, IntPtr.Zero, IntPtr.Zero);

        IntPtr desktopHost = IntPtr.Zero;
        writeLog("workerw selection step 3: enumerating top-level windows for SHELLDLL_DefView");
        _ = EnumWindows(
            (topLevelWindow, _) =>
            {
                var defView = FindWindowEx(topLevelWindow, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (defView == IntPtr.Zero)
                {
                    return true;
                }

                desktopHost = topLevelWindow;
                writeLog(
                    $"workerw selection step 3: desktop host hwnd={FormatHandle(desktopHost)} " +
                    $"class={GetWindowClassName(desktopHost)} defView={FormatHandle(defView)}");
                return false;
            },
            IntPtr.Zero);

        if (desktopHost == IntPtr.Zero)
        {
            reason = "workerw target not found";
            writeLog(reason);
            return false;
        }

        parent = FindWindowEx(IntPtr.Zero, desktopHost, "WorkerW", null);
        writeLog(
            $"workerw selection step 4: next WorkerW hwnd={FormatHandle(parent)} " +
            $"class={GetWindowClassName(parent)}");
        if (parent == IntPtr.Zero)
        {
            reason = "workerw target not found";
            writeLog(reason);
            return false;
        }

        reason = string.Empty;
        return true;
    }

    public static bool TryAttachToParent(IntPtr window, IntPtr parent, Action<string> writeLog, out string reason)
    {
        var beforeRect = GetWindowRectangle(window);
        var beforeStyle = GetStyleSnapshot(window);
        var beforeDpi = GetDpiSnapshot(window);
        writeLog(
            $"SetParent before rect={FormatRectangle(beforeRect)} style={FormatStyle(beforeStyle.Style)} " +
            $"exstyle={FormatStyle(beforeStyle.ExStyle)} dpiAwareness={beforeDpi.Awareness} monitorDpi={beforeDpi.Dpi}");

        Marshal.SetLastPInvokeError(0);
        var previousParent = SetParent(window, parent);
        var setParentError = Marshal.GetLastPInvokeError();
        var setParentSucceeded = previousParent != IntPtr.Zero || setParentError == 0;
        writeLog(
            $"SetParent success={setParentSucceeded} previousParent={FormatHandle(previousParent)} " +
            $"lastError={setParentError}");
        if (!setParentSucceeded)
        {
            reason = $"SetParent failed (Win32 {setParentError})";
            return false;
        }

        var childStyle = (beforeStyle.Style & ~WsPopup) | WsChild;
        var setStyleResult = SetWindowLongPtrChecked(window, GwlStyle, childStyle);
        if (!setStyleResult.Success)
        {
            reason = $"child style switch failed (Win32 {setStyleResult.LastError})";
            writeLog(reason);
            return false;
        }

        var parentRelativeLocation = new Point { X = beforeRect.Left, Y = beforeRect.Top };
        if (!ScreenToClient(parent, ref parentRelativeLocation))
        {
            reason = $"ScreenToClient failed (Win32 {Marshal.GetLastPInvokeError()})";
            writeLog(reason);
            return false;
        }

        if (!SetWindowPos(
                window,
                IntPtr.Zero,
                parentRelativeLocation.X,
                parentRelativeLocation.Y,
                0,
                0,
                SwpFrameChanged | SwpNoSize | SwpNoZOrder | SwpNoActivate))
        {
            reason = $"child reposition failed (Win32 {Marshal.GetLastPInvokeError()})";
            writeLog(reason);
            return false;
        }

        var afterRect = GetWindowRectangle(window);
        var afterStyle = GetStyleSnapshot(window);
        var afterDpi = GetDpiSnapshot(window);
        writeLog(
            $"SetParent after rect={FormatRectangle(afterRect)} style={FormatStyle(afterStyle.Style)} " +
            $"exstyle={FormatStyle(afterStyle.ExStyle)} dpiAwareness={afterDpi.Awareness} monitorDpi={afterDpi.Dpi}");

        reason = string.Empty;
        return true;
    }

    public static InteropOperationResult RestoreTopLevelStyle(IntPtr window)
    {
        Marshal.SetLastPInvokeError(0);
        _ = SetParent(window, IntPtr.Zero);
        var setParentError = Marshal.GetLastPInvokeError();
        if (setParentError != 0)
        {
            return new InteropOperationResult(false, setParentError, "SetParent to desktop failed");
        }

        var beforeStyle = GetWindowLongPtr(window, GwlStyle);
        var topLevelStyle = (beforeStyle | WsPopup) & ~WsChild;
        var setStyleResult = SetWindowLongPtrChecked(window, GwlStyle, topLevelStyle);
        if (!setStyleResult.Success)
        {
            return new InteropOperationResult(false, setStyleResult.LastError, "top-level style switch failed");
        }

        if (!SetWindowPos(window, IntPtr.Zero, 0, 0, 0, 0, SwpFrameChanged | SwpNoMove | SwpNoSize | SwpNoZOrder))
        {
            return new InteropOperationResult(false, Marshal.GetLastPInvokeError(), "top-level frame refresh failed");
        }

        return new InteropOperationResult(
            true,
            0,
            $"style before={FormatStyle(beforeStyle)} after={FormatStyle(topLevelStyle)}");
    }

    public static InteropOperationResult MoveToBottommost(IntPtr window)
    {
        if (!SetWindowPos(window, HwndBottom, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate))
        {
            return new InteropOperationResult(false, Marshal.GetLastPInvokeError(), "SetWindowPos(HWND_BOTTOM) failed");
        }

        return new InteropOperationResult(true, 0, "SetWindowPos(HWND_BOTTOM) completed");
    }

    // This only runs during a normal process shutdown. Forced termination (Task Manager) or a crash
    // does not execute this code, so stale pixels can remain; that is an inherent limitation of this technique.
    public static void RequestDesktopRepaint(IntPtr parent, Action<string> writeLog)
    {
        if (parent == IntPtr.Zero)
        {
            writeLog("desktop repaint skipped: parent is null");
            return;
        }

        if (!IsWindow(parent))
        {
            writeLog($"desktop repaint skipped: parent is invalid hwnd={FormatHandle(parent)}");
            return;
        }

        Marshal.SetLastPInvokeError(0);
        var success = RedrawWindow(
            parent,
            IntPtr.Zero,
            IntPtr.Zero,
            RdwInvalidate | RdwErase | RdwAllChildren | RdwUpdateNow);
        var lastError = success ? 0 : Marshal.GetLastPInvokeError();
        writeLog($"desktop repaint success={success} parent={FormatHandle(parent)} lastError={lastError}");
    }

    public static bool IsWindow(IntPtr window) => IsWindowNative(window);

    public static string GetWindowClassName(IntPtr window)
    {
        if (window == IntPtr.Zero)
        {
            return "<none>";
        }

        var className = new StringBuilder(256);
        return GetClassName(window, className, className.Capacity) == 0 ? "<unknown>" : className.ToString();
    }

    public static DpiSnapshot GetDpiSnapshot(IntPtr window)
    {
        var context = GetWindowDpiAwarenessContext(window);
        return new DpiSnapshot(GetAwarenessFromDpiAwarenessContext(context), GetDpiForWindow(window));
    }

    public static string FormatHandle(IntPtr handle) => $"0x{handle.ToInt64():X}";

    private static StyleSnapshot GetStyleSnapshot(IntPtr window) =>
        new(GetWindowLongPtr(window, GwlStyle), GetWindowLongPtr(window, GwlExStyle));

    private static Rectangle GetWindowRectangle(IntPtr window)
    {
        _ = GetWindowRect(window, out var rectangle);
        return rectangle;
    }

    private static InteropOperationResult SetWindowLongPtrChecked(IntPtr window, int index, nint value)
    {
        Marshal.SetLastPInvokeError(0);
        var previousValue = SetWindowLongPtr(window, index, value);
        var lastError = Marshal.GetLastPInvokeError();
        return previousValue == IntPtr.Zero && lastError != 0
            ? new InteropOperationResult(false, lastError, $"index={index} value={FormatStyle(value)}")
            : new InteropOperationResult(true, 0, $"index={index} value={FormatStyle(value)}");
    }

    private static string FormatRectangle(Rectangle rectangle) =>
        $"({rectangle.Left},{rectangle.Top})-({rectangle.Right},{rectangle.Bottom})";

    private static string FormatStyle(nint value) => $"0x{unchecked((ulong)value):X}";

    internal sealed record InteropOperationResult(bool Success, int LastError, string Detail);

    internal readonly record struct StyleSnapshot(nint Style, nint ExStyle);

    internal readonly record struct DpiSnapshot(int Awareness, uint Dpi);

    // https://learn.microsoft.com/windows/win32/api/winuser/ns-winuser-windowpos
    [StructLayout(LayoutKind.Sequential)]
    internal struct WindowPos
    {
        public IntPtr Hwnd;

        public IntPtr HwndInsertAfter;

        public int X;

        public int Y;

        public int Cx;

        public int Cy;

        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;

        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rectangle
    {
        public int Left;

        public int Top;

        public int Right;

        public int Bottom;
    }

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr lParam);

    // https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-findwindoww
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    // https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-sendmessagew
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    // https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-enumwindows
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    // https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-findwindowexw
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string lpClassName, string? windowName);

    // https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getwindowlongptrw
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr(IntPtr window, int index);

    // https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setwindowlongptrw
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, nint newLong);

    // https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setwindowpos
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    // https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setparent
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr child, IntPtr newParent);

    // https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-redrawwindow
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RedrawWindow(IntPtr window, IntPtr updateRectangle, IntPtr updateRegion, uint flags);

    // https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getwindowrect
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out Rectangle rectangle);

    // https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-screentoclient
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(IntPtr window, ref Point point);

    // https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-iswindow
    [DllImport("user32.dll", EntryPoint = "IsWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowNative(IntPtr window);

    // https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getclassnamew
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassName(IntPtr window, StringBuilder className, int maxCount);

    // https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getdpiforwindow
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    // https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getwindowdpiawarenesscontext
    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowDpiAwarenessContext(IntPtr window);

    // https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getawarenessfromdpiawarenesscontext
    [DllImport("user32.dll")]
    private static extern int GetAwarenessFromDpiAwarenessContext(IntPtr dpiContext);
}
