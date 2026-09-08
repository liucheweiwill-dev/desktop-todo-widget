using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopTodoWidget.Data;

public sealed class TodoDocument
{
    public const int CurrentSchemaVersion = 1;

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    [JsonPropertyName("items")]
    public List<TodoItem> Items { get; set; } = [];
}

public sealed class TodoItem
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("isDone")]
    public bool IsDone { get; set; }

    [JsonPropertyName("createdUtc")]
    public DateTimeOffset CreatedUtc { get; set; }
}

public sealed class TodoDocumentStore
{
    private readonly string _filePath;
    private readonly AtomicJsonStore<TodoDocumentData> _store;

    public TodoDocumentStore(string filePath)
    {
        _filePath = string.IsNullOrWhiteSpace(filePath)
            ? throw new ArgumentException("A todo storage file path is required.", nameof(filePath))
            : filePath;
        _store = new AtomicJsonStore<TodoDocumentData>(_filePath);
    }

    public void Write(TodoDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        Normalize(document);
        document.SchemaVersion = TodoDocument.CurrentSchemaVersion;
        _store.Write(ToData(document));
    }

    public TodoDocumentReadResult Read(TodoDocument defaultDocument)
    {
        ArgumentNullException.ThrowIfNull(defaultDocument);

        if (TryGetFutureSchemaVersion(_filePath, out var futureSchemaVersion))
        {
            return new TodoDocumentReadResult(
                defaultDocument,
                RecoveredFromBackup: false,
                HadInvalidData: false,
                UnsupportedSchemaVersion: futureSchemaVersion);
        }

        var readResult = _store.Read(ToData(defaultDocument));
        if (readResult.RecoveredFromBackup &&
            TryGetFutureSchemaVersion($"{_filePath}.bak", out futureSchemaVersion))
        {
            return new TodoDocumentReadResult(
                defaultDocument,
                RecoveredFromBackup: false,
                HadInvalidData: readResult.HadInvalidData,
                UnsupportedSchemaVersion: futureSchemaVersion);
        }

        var document = ToDocument(readResult.Value);
        MigrateToCurrentSchema(document);

        return new TodoDocumentReadResult(
            document,
            readResult.RecoveredFromBackup,
            readResult.HadInvalidData,
            UnsupportedSchemaVersion: null);
    }

    private static TodoDocumentData ToData(TodoDocument document)
    {
        var items = new List<TodoItemData?>();
        if (document.Items is not null)
        {
            foreach (var item in document.Items)
            {
                if (item is not null)
                {
                    items.Add(new TodoItemData
                    {
                        Id = item.Id,
                        Text = item.Text ?? string.Empty,
                        IsDone = item.IsDone,
                        CreatedUtc = item.CreatedUtc
                    });
                }
            }
        }

        return new TodoDocumentData
        {
            SchemaVersion = document.SchemaVersion,
            Items = items
        };
    }

    private static TodoDocument ToDocument(TodoDocumentData data)
    {
        var document = new TodoDocument
        {
            SchemaVersion = data.SchemaVersion ?? TodoDocument.CurrentSchemaVersion,
            Items = []
        };

        if (data.Items is not null)
        {
            foreach (var itemData in data.Items)
            {
                if (itemData is not null)
                {
                    document.Items.Add(new TodoItem
                    {
                        Id = itemData.Id ?? Guid.Empty,
                        Text = itemData.Text ?? string.Empty,
                        IsDone = itemData.IsDone ?? false,
                        CreatedUtc = itemData.CreatedUtc ?? default
                    });
                }
            }
        }

        return document;
    }

    private static void Normalize(TodoDocument document)
    {
        document.Items ??= [];
        document.Items.RemoveAll(static item => item is null);

        foreach (var item in document.Items)
        {
            item.Text ??= string.Empty;
        }
    }

    private static void MigrateToCurrentSchema(TodoDocument document)
    {
        if (document.SchemaVersion >= TodoDocument.CurrentSchemaVersion)
        {
            return;
        }

        // Version 1 introduced the initial document shape, so older documents need no field transform.
        // Future migrations are added here before the schema version is advanced.
        document.SchemaVersion = TodoDocument.CurrentSchemaVersion;
    }

    private static bool TryGetFutureSchemaVersion(string filePath, out int futureSchemaVersion)
    {
        futureSchemaVersion = default;

        if (!File.Exists(filePath))
        {
            return false;
        }

        try
        {
            using var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var json = JsonDocument.Parse(file);
            if (json.RootElement.ValueKind != JsonValueKind.Object ||
                !json.RootElement.TryGetProperty("schemaVersion", out var schemaVersionElement) ||
                schemaVersionElement.ValueKind != JsonValueKind.Number ||
                !schemaVersionElement.TryGetInt32(out var schemaVersion) ||
                schemaVersion <= TodoDocument.CurrentSchemaVersion)
            {
                return false;
            }

            futureSchemaVersion = schemaVersion;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            // Invalid or inaccessible data is handled by AtomicJsonStore's recovery path.
            return false;
        }
    }
}

public sealed record TodoDocumentReadResult(
    TodoDocument Document,
    bool RecoveredFromBackup,
    bool HadInvalidData,
    int? UnsupportedSchemaVersion);

internal sealed class TodoDocumentData
{
    [JsonPropertyName("schemaVersion")]
    public int? SchemaVersion { get; set; }

    [JsonPropertyName("items")]
    public List<TodoItemData?>? Items { get; set; }
}

internal sealed class TodoItemData
{
    [JsonPropertyName("id")]
    public Guid? Id { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("isDone")]
    public bool? IsDone { get; set; }

    [JsonPropertyName("createdUtc")]
    public DateTimeOffset? CreatedUtc { get; set; }
}
