using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DesktopTodoWidget.Data;

public sealed class TodoListViewModel : INotifyPropertyChanged
{
    public const int MaximumTextLength = 500;

    private readonly Action? _onChanged;

    public TodoListViewModel(
        IEnumerable<TodoItem>? initialItems = null,
        Action? onChanged = null)
    {
        _onChanged = onChanged;
        Items = new ObservableCollection<TodoListItemViewModel>();
        Items.CollectionChanged += Items_CollectionChanged;

        if (initialItems is null)
        {
            return;
        }

        foreach (var item in initialItems)
        {
            if (item is not null)
            {
                Items.Add(new TodoListItemViewModel(item));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<TodoListItemViewModel> Items { get; }

    public bool HasItems => Items.Count > 0;

    public TodoListItemViewModel? EditingItem => Items.FirstOrDefault(item => item.IsEditing);

    public TodoListItemViewModel? Add(string text)
    {
        var normalizedText = NormalizeText(text);
        if (normalizedText.Length == 0)
        {
            return null;
        }

        var item = new TodoListItemViewModel(new TodoItem
        {
            Id = Guid.NewGuid(),
            Text = normalizedText,
            IsDone = false,
            CreatedUtc = DateTimeOffset.UtcNow
        });
        Items.Add(item);
        NotifyChanged();
        return item;
    }

    public bool Remove(TodoListItemViewModel item)
    {
        if (item is null || !Items.Remove(item))
        {
            return false;
        }

        NotifyChanged();
        return true;
    }

    public bool ToggleDone(TodoListItemViewModel item)
    {
        if (item is null || !Items.Contains(item))
        {
            return false;
        }

        item.SetDone(!item.IsDone);
        NotifyChanged();
        return true;
    }

    public bool Rename(TodoListItemViewModel item, string text)
    {
        if (item is null || !Items.Contains(item))
        {
            return false;
        }

        var normalizedText = NormalizeText(text);
        item.EndEditing();
        if (normalizedText.Length == 0 || string.Equals(item.Text, normalizedText, StringComparison.Ordinal))
        {
            return false;
        }

        item.SetText(normalizedText);
        NotifyChanged();
        return true;
    }

    public bool StartEditing(TodoListItemViewModel item)
    {
        if (item is null || !Items.Contains(item))
        {
            return false;
        }

        foreach (var candidate in Items)
        {
            if (!ReferenceEquals(candidate, item))
            {
                candidate.EndEditing();
            }
        }

        item.BeginEditing();
        OnPropertyChanged(nameof(EditingItem));
        return true;
    }

    public void CancelEditing(TodoListItemViewModel item)
    {
        if (item is null || !Items.Contains(item))
        {
            return;
        }

        item.EndEditing();
        OnPropertyChanged(nameof(EditingItem));
    }

    public TodoDocument CreateDocument()
    {
        return new TodoDocument
        {
            Items = Items.Select(item => item.ToTodoItem()).ToList()
        };
    }

    private static string NormalizeText(string text)
    {
        var trimmedText = text?.Trim() ?? string.Empty;
        return trimmedText.Length <= MaximumTextLength
            ? trimmedText
            : trimmedText[..MaximumTextLength];
    }

    private void Items_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(EditingItem));
    }

    private void NotifyChanged()
    {
        _onChanged?.Invoke();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class TodoListItemViewModel : INotifyPropertyChanged
{
    private string _text;
    private bool _isDone;
    private bool _isEditing;
    private string _editingText = string.Empty;

    internal TodoListItemViewModel(TodoItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        Id = item.Id;
        _text = item.Text ?? string.Empty;
        _isDone = item.IsDone;
        CreatedUtc = item.CreatedUtc;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id { get; }

    public string Text => _text;

    public bool IsDone => _isDone;

    public DateTimeOffset CreatedUtc { get; }

    public bool IsEditing => _isEditing;

    public string EditingText
    {
        get => _editingText;
        set
        {
            var normalizedValue = value ?? string.Empty;
            if (string.Equals(_editingText, normalizedValue, StringComparison.Ordinal))
            {
                return;
            }

            _editingText = normalizedValue;
            OnPropertyChanged();
        }
    }

    internal void SetText(string text)
    {
        if (string.Equals(_text, text, StringComparison.Ordinal))
        {
            return;
        }

        _text = text;
        OnPropertyChanged(nameof(Text));
    }

    internal void SetDone(bool isDone)
    {
        if (_isDone == isDone)
        {
            return;
        }

        _isDone = isDone;
        OnPropertyChanged(nameof(IsDone));
    }

    internal void BeginEditing()
    {
        EditingText = Text;
        if (_isEditing)
        {
            return;
        }

        _isEditing = true;
        OnPropertyChanged(nameof(IsEditing));
    }

    internal void EndEditing()
    {
        EditingText = Text;
        if (!_isEditing)
        {
            return;
        }

        _isEditing = false;
        OnPropertyChanged(nameof(IsEditing));
    }

    internal TodoItem ToTodoItem()
    {
        return new TodoItem
        {
            Id = Id,
            Text = Text,
            IsDone = IsDone,
            CreatedUtc = CreatedUtc
        };
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
