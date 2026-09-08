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
    private const double ItemDragThreshold = 5;
    private const double AutoScrollEdgeHeight = 20;
    private const double AutoScrollStep = 10;

    private readonly AtomicJsonStore<WindowPlacement> _placementStore;
    private readonly DispatcherTimer _placementSaveTimer;
    private readonly TodoDocumentStore _todoStore;
    private readonly TodoListViewModel _todoList;
    private readonly DispatcherTimer _todoSaveTimer;
    private readonly DispatcherTimer _itemAutoScrollTimer;
    private readonly bool _todosAreReadOnly;
    private IntPtr _windowHandle;
    private HwndSource? _windowSource;
    private HwndSourceHook? _bottommostZOrderHook;
    private bool _isDragInProgress;
    private bool _isItemDragCandidate;
    private bool _isItemDragInProgress;
    private bool _placementSavePending;
    private bool _todoSavePending;
    private TodoListItemViewModel? _draggedTodoItem;
    private Point _itemDragStartPoint;
    private int _dragSourceIndex = -1;
    private int _dragTargetIndex = -1;

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
        PreviewMouseMove += MainWindow_PreviewMouseMove;
        PreviewMouseLeftButtonUp += MainWindow_PreviewMouseLeftButtonUp;
        TodoItemsControl.LostMouseCapture += TodoItemsControl_LostMouseCapture;

        _placementSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _placementSaveTimer.Tick += PlacementSaveTimer_Tick;
        _todoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _todoSaveTimer.Tick += TodoSaveTimer_Tick;
        _itemAutoScrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _itemAutoScrollTimer.Tick += ItemAutoScrollTimer_Tick;

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
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        var source = e.OriginalSource as DependencyObject;
        if (IsDescendantOf(source, HelpButton) || IsDescendantOf(source, HelpPopup))
        {
            return;
        }

        if (IsDescendantOf(source, WindowDragStrip))
        {
            MoveWindow();
            e.Handled = true;
            return;
        }

        if (FindTodoItem(source) is not null || IsInsideControl(source))
        {
            return;
        }

        MoveWindow();
        e.Handled = true;
    }

    private void MoveWindow()
    {
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

    private static TodoListItemViewModel? FindTodoItem(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is FrameworkElement { DataContext: TodoListItemViewModel item })
            {
                return item;
            }

            element = GetParent(element);
        }

        return null;
    }

    private static bool IsDescendantOf(DependencyObject? element, DependencyObject ancestor)
    {
        while (element is not null)
        {
            if (ReferenceEquals(element, ancestor))
            {
                return true;
            }

            element = GetParent(element);
        }

        return false;
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

    private void TodoItemGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_todosAreReadOnly ||
            e.ChangedButton != MouseButton.Left ||
            sender is not Grid { DataContext: TodoListItemViewModel item } ||
            IsItemDragExcluded(e.OriginalSource as DependencyObject))
        {
            return;
        }

        CancelItemDrag();
        _draggedTodoItem = item;
        _dragSourceIndex = _todoList.Items.IndexOf(item);
        _dragTargetIndex = _dragSourceIndex;
        _itemDragStartPoint = e.GetPosition(TaskListScrollViewer);
        _isItemDragCandidate = _dragSourceIndex >= 0;
    }

    private static bool IsItemDragExcluded(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is CheckBox or TextBoxBase or PasswordBox or ComboBox or ScrollBar or Thumb or Slider)
            {
                return true;
            }

            if (element is Button button && !string.Equals(button.Name, "TodoItemTextButton", StringComparison.Ordinal))
            {
                return true;
            }

            element = GetParent(element);
        }

        return false;
    }

    private void MainWindow_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isItemDragCandidate && !_isItemDragInProgress)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            CancelItemDrag();
            return;
        }

        var pointerPosition = e.GetPosition(TaskListScrollViewer);
        if (!_isItemDragInProgress)
        {
            if (Math.Abs(pointerPosition.Y - _itemDragStartPoint.Y) <= ItemDragThreshold)
            {
                return;
            }

            BeginItemDrag();
        }

        if (!_isItemDragInProgress)
        {
            return;
        }

        UpdateItemDrag(pointerPosition);
        e.Handled = true;
    }

    private void BeginItemDrag()
    {
        if (!_isItemDragCandidate || _draggedTodoItem is null)
        {
            CancelItemDrag();
            return;
        }

        _dragSourceIndex = _todoList.Items.IndexOf(_draggedTodoItem);
        if (_dragSourceIndex < 0 || !Mouse.Capture(TodoItemsControl))
        {
            CancelItemDrag();
            return;
        }

        _dragTargetIndex = _dragSourceIndex;
        _isItemDragCandidate = false;
        _isItemDragInProgress = true;
    }

    private void UpdateItemDrag(Point pointerPosition)
    {
        var insertionIndex = FindItemInsertionIndex(pointerPosition);
        _dragTargetIndex = insertionIndex > _dragSourceIndex
            ? insertionIndex - 1
            : insertionIndex;

        ShowDragInsertionIndicator(insertionIndex);
        UpdateItemAutoScroll(pointerPosition);
    }

    private int FindItemInsertionIndex(Point pointerPosition)
    {
        var itemPoint = TaskListScrollViewer.TranslatePoint(pointerPosition, TodoItemsControl);
        for (var index = 0; index < _todoList.Items.Count; index++)
        {
            var bounds = GetTodoItemBounds(index, TodoItemsControl);
            if (bounds is not null && itemPoint.Y < bounds.Value.Top + (bounds.Value.Height / 2))
            {
                return index;
            }
        }

        return _todoList.Items.Count;
    }

    private void ShowDragInsertionIndicator(int insertionIndex)
    {
        if (_todoList.Items.Count == 0)
        {
            DragInsertionIndicator.Visibility = Visibility.Collapsed;
            return;
        }

        var referenceIndex = insertionIndex < _todoList.Items.Count
            ? insertionIndex
            : _todoList.Items.Count - 1;
        var bounds = GetTodoItemBounds(referenceIndex, TaskListPanel);
        if (bounds is null)
        {
            DragInsertionIndicator.Visibility = Visibility.Collapsed;
            return;
        }

        var top = insertionIndex < _todoList.Items.Count
            ? bounds.Value.Top
            : bounds.Value.Bottom;
        DragInsertionIndicator.Width = TaskListPanel.ActualWidth;
        Canvas.SetLeft(DragInsertionIndicator, 0);
        Canvas.SetTop(DragInsertionIndicator, top - (DragInsertionIndicator.Height / 2));
        DragInsertionIndicator.Visibility = Visibility.Visible;
    }

    private Rect? GetTodoItemBounds(int index, Visual ancestor)
    {
        if (TodoItemsControl.ItemContainerGenerator.ContainerFromIndex(index) is not FrameworkElement container)
        {
            return null;
        }

        var topLeft = container.TransformToAncestor(ancestor).Transform(new Point());
        return new Rect(topLeft, container.RenderSize);
    }

    private void UpdateItemAutoScroll(Point pointerPosition)
    {
        if (GetItemAutoScrollDirection(pointerPosition) == 0)
        {
            _itemAutoScrollTimer.Stop();
            return;
        }

        _itemAutoScrollTimer.Start();
    }

    private int GetItemAutoScrollDirection(Point pointerPosition)
    {
        if (TaskListScrollViewer.ScrollableHeight <= 0 || TaskListScrollViewer.ActualHeight <= 0)
        {
            return 0;
        }

        if (pointerPosition.Y <= AutoScrollEdgeHeight)
        {
            return -1;
        }

        if (pointerPosition.Y >= TaskListScrollViewer.ActualHeight - AutoScrollEdgeHeight)
        {
            return 1;
        }

        return 0;
    }

    private void ItemAutoScrollTimer_Tick(object? sender, EventArgs e)
    {
        if (!_isItemDragInProgress)
        {
            _itemAutoScrollTimer.Stop();
            return;
        }

        var pointerPosition = Mouse.GetPosition(TaskListScrollViewer);
        var direction = GetItemAutoScrollDirection(pointerPosition);
        if (direction == 0)
        {
            _itemAutoScrollTimer.Stop();
            return;
        }

        var nextOffset = Math.Clamp(
            TaskListScrollViewer.VerticalOffset + (direction * AutoScrollStep),
            0,
            TaskListScrollViewer.ScrollableHeight);
        if (nextOffset == TaskListScrollViewer.VerticalOffset)
        {
            _itemAutoScrollTimer.Stop();
            return;
        }

        TaskListScrollViewer.ScrollToVerticalOffset(nextOffset);
        UpdateItemDrag(pointerPosition);
    }

    private void MainWindow_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || (!_isItemDragCandidate && !_isItemDragInProgress))
        {
            return;
        }

        if (!_isItemDragInProgress)
        {
            CancelItemDrag();
            return;
        }

        UpdateItemDrag(e.GetPosition(TaskListScrollViewer));
        var fromIndex = _draggedTodoItem is null
            ? -1
            : _todoList.Items.IndexOf(_draggedTodoItem);
        var toIndex = _dragTargetIndex;
        CompleteItemDrag();
        _todoList.Move(fromIndex, toIndex);
        e.Handled = true;
    }

    private void TodoItemsControl_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_isItemDragInProgress)
        {
            CancelItemDrag();
        }
    }

    private void CompleteItemDrag()
    {
        ClearItemDragState();
    }

    private void CancelItemDrag()
    {
        ClearItemDragState();
    }

    private void ClearItemDragState()
    {
        _isItemDragCandidate = false;
        _isItemDragInProgress = false;
        _draggedTodoItem = null;
        _dragSourceIndex = -1;
        _dragTargetIndex = -1;
        _itemAutoScrollTimer.Stop();
        DragInsertionIndicator.Visibility = Visibility.Collapsed;

        if (ReferenceEquals(Mouse.Captured, TodoItemsControl))
        {
            Mouse.Capture(null);
        }
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

    private void HelpButton_Click(object sender, RoutedEventArgs e)
    {
        HelpPopup.IsOpen = !HelpPopup.IsOpen;
    }

    private void TodoItemStyleButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Popup popup })
        {
            CancelItemDrag();
            popup.IsOpen = !popup.IsOpen;
        }
    }

    private void TodoItemStylePopup_Opened(object? sender, EventArgs e)
    {
        CancelItemDrag();
    }

    private void TodoItemStylePopup_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // The popup is outside the item visual tree, but always cancel an active drag before its controls act.
        CancelItemDrag();
    }

    private void DecreaseTodoItemFontSizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: TodoListItemViewModel item })
        {
            _todoList.SetFontSize(item, item.EffectiveFontSize - 1);
        }
    }

    private void IncreaseTodoItemFontSizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: TodoListItemViewModel item })
        {
            _todoList.SetFontSize(item, item.EffectiveFontSize + 1);
        }
    }

    private void TodoItemColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: TodoListItemViewModel item, Tag: string colorKey })
        {
            _todoList.SetColorKey(item, colorKey);
        }
    }

    private void ResetTodoItemStyleButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: TodoListItemViewModel item })
        {
            _todoList.ResetStyle(item);
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

        if (_isItemDragInProgress)
        {
            CancelItemDrag();
            e.Handled = true;
            return;
        }

        if (HelpPopup.IsOpen)
        {
            HelpPopup.IsOpen = false;
            e.Handled = true;
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
        CancelItemDrag();
        CommitActiveTodoEdit();
        SavePendingTodos();
        SaveCurrentPlacement(force: true);
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _placementSaveTimer.Stop();
        _todoSaveTimer.Stop();
        _itemAutoScrollTimer.Stop();
        RemoveBottommostZOrderHook();
    }
}
