using System.Runtime.InteropServices;

namespace DesktopTodoWidget.Interop;

internal static class DesktopAttach
{
    private const int GwlExStyle = -20;
    private const nint WsExToolWindow = 0x00000080;
    private const nint WsExAppWindow = 0x00040000;
    internal const int WmWindowPosChanging = 0x0046;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    internal const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    internal static readonly IntPtr HwndBottom = new(1);
    private const uint MonitorInfoFPrimary = 0x00000001;

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

    public static InteropOperationResult MoveToBottommost(IntPtr window)
    {
        if (!SetWindowPos(window, HwndBottom, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate))
        {
            return new InteropOperationResult(false, Marshal.GetLastPInvokeError(), "SetWindowPos(HWND_BOTTOM) failed");
        }

        return new InteropOperationResult(true, 0, "SetWindowPos(HWND_BOTTOM) completed");
    }

    internal static IReadOnlyList<MonitorWorkingArea> GetMonitorWorkingAreas()
    {
        var workingAreas = new List<MonitorWorkingArea>();

        _ = EnumDisplayMonitors(
            IntPtr.Zero,
            IntPtr.Zero,
            (monitor, monitorDeviceContext, monitorRectangle, data) =>
            {
                var monitorInfo = new MonitorInfo
                {
                    CbSize = (uint)Marshal.SizeOf<MonitorInfo>()
                };

                if (GetMonitorInfo(monitor, ref monitorInfo))
                {
                    var workArea = monitorInfo.RcWork;
                    workingAreas.Add(
                        new MonitorWorkingArea(
                            workArea.Left,
                            workArea.Top,
                            workArea.Right - workArea.Left,
                            workArea.Bottom - workArea.Top,
                            (monitorInfo.DwFlags & MonitorInfoFPrimary) != 0));
                }

                return true;
            },
            IntPtr.Zero);

        return workingAreas;
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

    private static string FormatStyle(nint value) => $"0x{unchecked((ulong)value):X}";

    internal sealed record InteropOperationResult(bool Success, int LastError, string Detail);

    internal readonly record struct MonitorWorkingArea(int Left, int Top, int Width, int Height, bool IsPrimary);

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

    // https://learn.microsoft.com/windows/win32/api/winuser/ns-winuser-monitorinfo
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint CbSize;

        public Rectangle RcMonitor;

        public Rectangle RcWork;

        public uint DwFlags;
    }

    // https://learn.microsoft.com/windows/win32/api/windef/ns-windef-rect
    [StructLayout(LayoutKind.Sequential)]
    private struct Rectangle
    {
        public int Left;

        public int Top;

        public int Right;

        public int Bottom;
    }

    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool MonitorEnumProc(
        IntPtr monitor,
        IntPtr monitorDeviceContext,
        IntPtr monitorRectangle,
        IntPtr data);

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

    // https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-enumdisplaymonitors
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(
        IntPtr deviceContext,
        IntPtr clippingRectangle,
        MonitorEnumProc callback,
        IntPtr data);

    // https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getmonitorinfow
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);
}
