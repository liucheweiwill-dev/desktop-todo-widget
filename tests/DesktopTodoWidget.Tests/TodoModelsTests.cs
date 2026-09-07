using System.IO;
using System.Text;
using System.Text.Json;
using DesktopTodoWidget.Data;
using Xunit;

namespace DesktopTodoWidget.Tests;

public sealed class TodoModelsTests
{
    [Fact]
    public void Write_andRead_preserveItemsAndTheirArrayOrder()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var filePath = temporaryDirectory.GetPath("todos.json");
        var store = new TodoDocumentStore(filePath);
        var expected = new TodoDocument
        {
            Items =
            [
                CreateItem("First", isDone: false, "2026-09-07T01:02:03+00:00"),
                CreateItem("Second", isDone: true, "2026-09-07T04:05:06+00:00")
            ]
        };

        store.Write(expected);
        var result = store.Read(new TodoDocument());

        Assert.Null(result.UnsupportedSchemaVersion);
        Assert.False(result.HadInvalidData);
        Assert.Equal(TodoDocument.CurrentSchemaVersion, result.Document.SchemaVersion);
        AssertItemsEqual(expected.Items, result.Document.Items);
    }

    [Fact]
    public void Write_alwaysIncludesTheCurrentSchemaVersion()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var filePath = temporaryDirectory.GetPath("todos.json");
        var store = new TodoDocumentStore(filePath);
        var document = new TodoDocument { SchemaVersion = TodoDocument.CurrentSchemaVersion + 10 };

        store.Write(document);

        using var json = JsonDocument.Parse(File.ReadAllText(filePath));
        Assert.Equal(TodoDocument.CurrentSchemaVersion, json.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(TodoDocument.CurrentSchemaVersion, document.SchemaVersion);
    }

    [Fact]
    public void Read_whenSchemaVersionIsNewer_rejectsItWithoutChangingTheFile()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var filePath = temporaryDirectory.GetPath("todos.json");
        var originalBytes = Encoding.UTF8.GetBytes(
            "{\"schemaVersion\":2,\"items\":[{\"id\":\"d3b07384-d9a0-4b8e-8d2f-0ecf3e0c9e12\",\"text\":\"Newer version\",\"isDone\":false,\"createdUtc\":\"2026-09-07T00:00:00+00:00\"}]}");
        File.WriteAllBytes(filePath, originalBytes);
        var store = new TodoDocumentStore(filePath);
        var defaultDocument = new TodoDocument { Items = [CreateItem("Default", false, "2026-09-01T00:00:00+00:00")] };

        var result = store.Read(defaultDocument);

        Assert.Equal(2, result.UnsupportedSchemaVersion!.Value);
        Assert.Same(defaultDocument, result.Document);
        Assert.False(result.HadInvalidData);
        Assert.Equal(originalBytes, File.ReadAllBytes(filePath));
    }

    [Fact]
    public void Read_whenFileIsCorrupt_reportsInvalidDataInsteadOfAnUnsupportedSchema()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var filePath = temporaryDirectory.GetPath("todos.json");
        File.WriteAllText(filePath, "{");
        var store = new TodoDocumentStore(filePath);

        var result = store.Read(new TodoDocument());

        Assert.Null(result.UnsupportedSchemaVersion);
        Assert.True(result.HadInvalidData);
    }

    [Fact]
    public void Read_ignoresUnknownFields()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var filePath = temporaryDirectory.GetPath("todos.json");
        File.WriteAllText(
            filePath,
            "{\"schemaVersion\":1,\"futureDocumentField\":\"ignored\",\"items\":[{\"id\":\"ebf09086-1a30-4d6b-887d-5d1f4b73a973\",\"text\":\"Keep me\",\"isDone\":true,\"createdUtc\":\"2026-09-07T00:00:00+00:00\",\"futureItemField\":42}]}");
        var store = new TodoDocumentStore(filePath);

        var result = store.Read(new TodoDocument());

        Assert.Null(result.UnsupportedSchemaVersion);
        var item = Assert.Single(result.Document.Items);
        Assert.Equal("Keep me", item.Text);
        Assert.True(item.IsDone);
    }

    [Fact]
    public void Read_whenFieldsAreMissingOrNull_usesDefaults()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var filePath = temporaryDirectory.GetPath("todos.json");
        File.WriteAllText(
            filePath,
            "{\"schemaVersion\":null,\"items\":[{\"id\":null,\"text\":null,\"createdUtc\":null},{\"id\":\"1e9afdd4-8a94-4d63-8636-237d7e27155b\",\"text\":\"Missing isDone\",\"createdUtc\":\"2026-09-07T00:00:00+00:00\"}]}");
        var store = new TodoDocumentStore(filePath);

        var result = store.Read(new TodoDocument());

        Assert.Null(result.UnsupportedSchemaVersion);
        Assert.Equal(TodoDocument.CurrentSchemaVersion, result.Document.SchemaVersion);
        Assert.Equal(2, result.Document.Items.Count);
        Assert.Equal(Guid.Empty, result.Document.Items[0].Id);
        Assert.Equal(string.Empty, result.Document.Items[0].Text);
        Assert.False(result.Document.Items[0].IsDone);
        Assert.Equal(default(DateTimeOffset), result.Document.Items[0].CreatedUtc);
        Assert.False(result.Document.Items[1].IsDone);
    }

    [Fact]
    public void Write_andRead_preserveChineseAndEmojiText()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var filePath = temporaryDirectory.GetPath("todos.json");
        var store = new TodoDocumentStore(filePath);
        var expectedText = "完成買牛奶 🥛✨";
        store.Write(new TodoDocument { Items = [CreateItem(expectedText, false, "2026-09-07T00:00:00+00:00")] });

        var result = store.Read(new TodoDocument());

        Assert.Equal(expectedText, Assert.Single(result.Document.Items).Text);
    }

    [Fact]
    public void Write_andRead_supportAnEmptyItemsArray()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var filePath = temporaryDirectory.GetPath("todos.json");
        var store = new TodoDocumentStore(filePath);

        store.Write(new TodoDocument { Items = [] });
        var result = store.Read(new TodoDocument { Items = [CreateItem("Default", false, "2026-09-07T00:00:00+00:00")] });

        Assert.Empty(result.Document.Items);
        Assert.Null(result.UnsupportedSchemaVersion);
    }

    [Fact]
    public void Write_andRead_preserveOrderAfterAnItemIsMovedInTheArray()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var filePath = temporaryDirectory.GetPath("todos.json");
        var store = new TodoDocumentStore(filePath);
        var document = new TodoDocument
        {
            Items =
            [
                CreateItem("One", false, "2026-09-07T00:00:00+00:00"),
                CreateItem("Two", false, "2026-09-07T00:00:01+00:00"),
                CreateItem("Three", false, "2026-09-07T00:00:02+00:00")
            ]
        };
        var movedItem = document.Items[2];
        document.Items.RemoveAt(2);
        document.Items.Insert(0, movedItem);

        store.Write(document);
        var result = store.Read(new TodoDocument());

        Assert.Equal(new[] { "Three", "One", "Two" }, result.Document.Items.Select(item => item.Text));
    }

    [Fact]
    public void Read_whenSchemaVersionIsOlder_migratesItToTheCurrentVersion()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var filePath = temporaryDirectory.GetPath("todos.json");
        File.WriteAllText(filePath, "{\"schemaVersion\":0,\"items\":[]}");
        var store = new TodoDocumentStore(filePath);

        var result = store.Read(new TodoDocument());

        Assert.Null(result.UnsupportedSchemaVersion);
        Assert.Equal(TodoDocument.CurrentSchemaVersion, result.Document.SchemaVersion);
    }

    private static TodoItem CreateItem(string text, bool isDone, string createdUtc)
    {
        return new TodoItem
        {
            Id = Guid.NewGuid(),
            Text = text,
            IsDone = isDone,
            CreatedUtc = DateTimeOffset.Parse(createdUtc)
        };
    }

    private static void AssertItemsEqual(IReadOnlyList<TodoItem> expected, IReadOnlyList<TodoItem> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            Assert.Equal(expected[index].Id, actual[index].Id);
            Assert.Equal(expected[index].Text, actual[index].Text);
            Assert.Equal(expected[index].IsDone, actual[index].IsDone);
            Assert.Equal(expected[index].CreatedUtc, actual[index].CreatedUtc);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Root = Path.Combine(Path.GetTempPath(), $"DesktopTodoWidget.Tests.{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string GetPath(string name) => Path.Combine(Root, name);

        public void Dispose()
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
