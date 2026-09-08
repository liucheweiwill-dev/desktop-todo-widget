using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using DesktopTodoWidget.Data;
using DesktopTodoWidget.Interop;

namespace DesktopTodoWidget;

public partial class MainWindow : Window
{
    private const double DefaultLeft = 80;
    private const double DefaultTop = 80;
    private const double WindowWidth = 360;
    private const double WindowHeight = 420;

    private readonly AtomicJsonStore<WindowPlacement> _placementStore;
    private readonly DispatcherTimer _placementSaveTimer;
    private readonly TodoDocumentStore _todoStore;
    private readonly TodoListViewModel _todoList;
    private readonly DispatcherTimer _todoSaveTimer;
    private readonly bool _todosAreReadOnly;
    private IntPtr _windowHandle;
    private HwndSource? _windowSource;
    private HwndSourceHook? _bottommostZOrderHook;
    private bool _isDragInProgress;
    private bool _placementSavePending;
    private bool _todoSavePending;

    internal MainWindow(
        AtomicJsonStore<WindowPlacement> placementStore,
        TodoDocumentStore todoStore)
    {
        _placementStore = placementStore ?? throw new ArgumentNullException(nameof(placementStore));
        _todoStore = todoStore ?? throw new ArgumentNullException(nameof(todoStore));
        var todoReadResult = _todoStore.Read(new TodoDocument());
        _todosAreReadOnly = todoReadResult.UnsupportedSchemaVersion is not null;
        _todoList = new TodoListViewModel(todoReadResult.Document.Items, ScheduleTodoSave);

        InitializeComponent();
        DataContext = _todoList;
        SourceInitialized += MainWindow_SourceInitialized;
        Closing += MainWindow_Closing;
        Closed += MainWindow_Closed;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        PreviewMouseLeftButtonDown += MainWindow_PreviewMouseLeftButtonDown;

        _placementSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _placementSaveTimer.Tick += PlacementSaveTimer_Tick;
        _todoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _todoSaveTimer.Tick += TodoSaveTimer_Tick;

        if (_todosAreReadOnly)
        {
            NewTaskTextBox.IsEnabled = false;
            TodoItemsControl.IsEnabled = false;
            ShowTodoStatus("ReadOnlySchemaText");
        }
        else if (todoReadResult.RecoveredFromBackup)
        {
            ShowTodoStatus("RecoveredFromBackupText");
        }
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        _windowHandle = new WindowInteropHelper(this).Handle;
        RestoreSavedPlacement();

        var toolWindowResult = DesktopAttach.ConfigureToolWindow(_windowHandle);
        Trace.WriteLine(
            $"DesktopTodoWidget tool-window style success={toolWindowResult.Success} " +
            $"lastError={toolWindowResult.LastError} detail={toolWindowResult.Detail}");

        InstallBottommostZOrderHook();
        var bottommostResult = DesktopAttach.MoveToBottommost(_windowHandle);
        Trace.WriteLine(
            $"DesktopTodoWidget bottommost success={bottommostResult.Success} " +
            $"lastError={bottommostResult.LastError} detail={bottommostResult.Detail}");
    }

    private void RestoreSavedPlacement()
    {
        var defaultBounds = new WindowRectangle(DefaultLeft, DefaultTop, WindowWidth, WindowHeight);
        var readResult = _placementStore.Read(new WindowPlacement
        {
            Left = DefaultLeft,
            Top = DefaultTop
        });
        var savedBounds = new WindowRectangle(
            readResult.Value.Left,
            readResult.Value.Top,
            WindowWidth,
            WindowHeight);
        var safeBounds = WindowPlacement.ClampToVisibleArea(
            savedBounds,
            GetWorkingAreas(),
            defaultBounds);

        Left = safeBounds.Left;
        Top = safeBounds.Top;

        if (readResult.HadInvalidData)
        {
            Trace.WriteLine("DesktopTodoWidget window placement data was invalid; a safe position was used.");
        }
    }

    private static IReadOnlyList<WindowRectangle> GetWorkingAreas()
    {
        var monitorWorkingAreas = DesktopAttach.GetMonitorWorkingAreas();
        var workingAreas = new List<WindowRectangle>(monitorWorkingAreas.Count);

        foreach (var workingArea in monitorWorkingAreas)
        {
            if (workingArea.IsPrimary)
            {
                workingAreas.Add(ToWindowRectangle(workingArea));
            }
        }

        if (workingAreas.Count == 0)
        {
            return [];
        }

        foreach (var workingArea in monitorWorkingAreas)
        {
            if (!workingArea.IsPrimary)
            {
                workingAreas.Add(ToWindowRectangle(workingArea));
            }
        }

        return workingAreas;
    }

    private static WindowRectangle ToWindowRectangle(DesktopAttach.MonitorWorkingArea workingArea) =>
        new(workingArea.Left, workingArea.Top, workingArea.Width, workingArea.Height);

    private void InstallBottommostZOrderHook()
    {
        if (_bottommostZOrderHook is not null)
        {
            return;
        }

        var windowSource = HwndSource.FromHwnd(_windowHandle);
        if (windowSource is null)
        {
            Trace.WriteLine("DesktopTodoWidget bottommost z-order hook was not attached because HwndSource was unavailable.");
            return;
        }

        _windowSource = windowSource;
        _bottommostZOrderHook = BottommostZOrderHook;
        _windowSource.AddHook(_bottommostZOrderHook);
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
    }

    private IntPtr BottommostZOrderHook(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (!_isDragInProgress && message == DesktopAttach.WmWindowPosChanging && lParam != IntPtr.Zero)
        {
            var windowPos = Marshal.PtrToStructure<DesktopAttach.WindowPos>(lParam);
            windowPos.HwndInsertAfter = DesktopAttach.HwndBottom;
            windowPos.Flags &= ~DesktopAttach.SwpNoZOrder;
            Marshal.StructureToPtr(windowPos, lParam, fDeleteOld: false);
        }

        return IntPtr.Zero;
    }

    private void MainWindow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || IsInsideControl(e.OriginalSource as DependencyObject))
        {
            return;
        }

        _isDragInProgress = true;
        try
        {
            DragMove();
        }
        finally
        {
            _isDragInProgress = false;
            var bottommostResult = DesktopAttach.MoveToBottommost(_windowHandle);
            Trace.WriteLine(
                $"DesktopTodoWidget bottommost after drag success={bottommostResult.Success} " +
                $"lastError={bottommostResult.LastError} detail={bottommostResult.Detail}");
        }

        SchedulePlacementSave();
    }

    private bool IsInsideControl(DependencyObject? element)
    {
        while (element is not null && !ReferenceEquals(element, this))
        {
            if (IsInteractiveControl(element))
            {
                return true;
            }

            element = GetParent(element);
        }

        return false;
    }

    private static bool IsInteractiveControl(DependencyObject element)
    {
        return element switch
        {
            ToggleButton toggleButton => toggleButton.IsHitTestVisible,
            ButtonBase => true,
            TextBoxBase => true,
            PasswordBox => true,
            ComboBox => true,
            ListBoxItem => true,
            MenuItem => true,
            ScrollBar => true,
            Thumb => true,
            Slider => true,
            _ => false
        };
    }

    private static DependencyObject? GetParent(DependencyObject element)
    {
        return element switch
        {
            Visual or Visual3D => VisualTreeHelper.GetParent(element),
            FrameworkContentElement contentElement => contentElement.Parent,
            _ => LogicalTreeHelper.GetParent(element)
        };
    }

    private void NewTaskTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        if (_todoList.Add(NewTaskTextBox.Text) is not null)
        {
            NewTaskTextBox.Clear();
            NewTaskTextBox.Focus();
        }

        e.Handled = true;
    }

    private void NewTaskTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        NewTaskPlaceholderTextBlock.Visibility = string.IsNullOrEmpty(NewTaskTextBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void TodoItemCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: TodoListItemViewModel item })
        {
            _todoList.ToggleDone(item);
        }
    }

    private void DeleteTodoItemButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: TodoListItemViewModel item })
        {
            _todoList.Remove(item);
        }
    }

    private void TodoItemTextButton_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button { DataContext: TodoListItemViewModel item })
        {
            return;
        }

        CommitActiveTodoEdit();
        if (!_todoList.StartEditing(item))
        {
            return;
        }

        e.Handled = true;
        Dispatcher.BeginInvoke(
            () => FocusTodoItemEditTextBox(item),
            DispatcherPriority.Background);
    }

    private void TodoItemEditTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox editTextBox)
        {
            return;
        }

        CommitTodoItemEdit(editTextBox);
        e.Handled = true;
    }

    private void TodoItemEditTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox editTextBox)
        {
            CommitTodoItemEdit(editTextBox);
        }
    }

    private void CommitActiveTodoEdit()
    {
        var editingItem = _todoList.EditingItem;
        if (editingItem is not null)
        {
            _todoList.Rename(editingItem, editingItem.EditingText);
        }
    }

    private void CommitTodoItemEdit(TextBox editTextBox)
    {
        if (editTextBox.DataContext is TodoListItemViewModel item && item.IsEditing)
        {
            _todoList.Rename(item, editTextBox.Text);
        }
    }

    private void FocusTodoItemEditTextBox(TodoListItemViewModel item)
    {
        if (!item.IsEditing)
        {
            return;
        }

        var editTextBox = FindTodoItemEditTextBox(TodoItemsControl, item);
        if (editTextBox is not null)
        {
            editTextBox.Focus();
            editTextBox.SelectAll();
        }
    }

    private static TextBox? FindTodoItemEditTextBox(
        DependencyObject parent,
        TodoListItemViewModel item)
    {
        for (var childIndex = 0; childIndex < VisualTreeHelper.GetChildrenCount(parent); childIndex++)
        {
            var child = VisualTreeHelper.GetChild(parent, childIndex);
            if (child is TextBox textBox && ReferenceEquals(textBox.DataContext, item))
            {
                return textBox;
            }

            var nestedTextBox = FindTodoItemEditTextBox(child, item);
            if (nestedTextBox is not null)
            {
                return nestedTextBox;
            }
        }

        return null;
    }

    private void ShowTodoStatus(string resourceKey)
    {
        if (FindResource(resourceKey) is string statusText)
        {
            TodoStatusTextBlock.Text = statusText;
            TodoStatusTextBlock.Visibility = Visibility.Visible;
        }
    }

    private void SchedulePlacementSave()
    {
        _placementSavePending = true;
        _placementSaveTimer.Stop();
        _placementSaveTimer.Start();
    }

    private void PlacementSaveTimer_Tick(object? sender, EventArgs e)
    {
        SaveCurrentPlacement(force: false);
    }

    private void ScheduleTodoSave()
    {
        if (_todosAreReadOnly)
        {
            return;
        }

        _todoSavePending = true;
        _todoSaveTimer.Stop();
        _todoSaveTimer.Start();
    }

    private void TodoSaveTimer_Tick(object? sender, EventArgs e)
    {
        SavePendingTodos();
    }

    private void SavePendingTodos()
    {
        _todoSaveTimer.Stop();
        if (!_todoSavePending)
        {
            return;
        }

        try
        {
            _todoStore.Write(_todoList.CreateDocument());
            _todoSavePending = false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Trace.WriteLine($"DesktopTodoWidget could not save todos: {exception.Message}");
        }
    }

    private void SaveCurrentPlacement(bool force)
    {
        _placementSaveTimer.Stop();
        if (!force && !_placementSavePending)
        {
            return;
        }

        try
        {
            _placementStore.Write(new WindowPlacement
            {
                Left = Left,
                Top = Top
            });
            _placementSavePending = false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Trace.WriteLine($"DesktopTodoWidget could not save window placement: {exception.Message}");
        }
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        var editingItem = _todoList.EditingItem;
        if (editingItem is not null)
        {
            _todoList.CancelEditing(editingItem);
            e.Handled = true;
            return;
        }

        e.Handled = true;
        Close();
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        CommitActiveTodoEdit();
        SavePendingTodos();
        SaveCurrentPlacement(force: true);
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _placementSaveTimer.Stop();
        _todoSaveTimer.Stop();
        RemoveBottommostZOrderHook();
    }
}
