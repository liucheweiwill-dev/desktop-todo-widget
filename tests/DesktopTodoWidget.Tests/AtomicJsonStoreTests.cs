using System.IO;
using System.Text.Json;
using DesktopTodoWidget.Data;
using Xunit;

namespace DesktopTodoWidget.Tests;

public sealed class AtomicJsonStoreTests
{
    [Fact]
    public void Write_forFirstSave_createsFileAndReadsItBack()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var filePath = temporaryDirectory.GetPath("state.json");
        var store = new AtomicJsonStore<SamplePayload>(filePath);
        var expected = new SamplePayload("First save", 1);
        Assert.False(File.Exists(filePath));

        store.Write(expected);
        var result = store.Read(new SamplePayload("Default", 0));

        Assert.True(File.Exists(filePath));
        Assert.Equal(expected, result.Value);
        Assert.False(result.RecoveredFromBackup);
        Assert.False(result.HadInvalidData);
    }

    [Fact]
    public void Write_overExistingFile_replacesItAndKeepsPreviousVersionInBackup()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var filePath = temporaryDirectory.GetPath("state.json");
        var store = new AtomicJsonStore<SamplePayload>(filePath);
        var firstVersion = new SamplePayload("First", 1);
        var secondVersion = new SamplePayload("Second", 2);
        store.Write(firstVersion);

        store.Write(secondVersion);

        Assert.Equal(secondVersion, store.Read(new SamplePayload("Default", 0)).Value);
        Assert.True(File.Exists($"{filePath}.bak"));
        var backup = JsonSerializer.Deserialize<SamplePayload>(File.ReadAllText($"{filePath}.bak"));
        Assert.Equal(firstVersion, backup);
    }

    [Fact]
    public void Read_whenFileDoesNotExist_returnsDefaultWithoutRecovery()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var defaultValue = new SamplePayload("Default", 0);
        var store = new AtomicJsonStore<SamplePayload>(temporaryDirectory.GetPath("missing.json"));

        var result = store.Read(defaultValue);

        Assert.Equal(defaultValue, result.Value);
        Assert.False(result.RecoveredFromBackup);
        Assert.False(result.HadInvalidData);
    }

    [Fact]
    public void Read_whenPrimaryFileIsCorrupt_recoversFromBackupAndReportsIt()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var filePath = temporaryDirectory.GetPath("state.json");
        var store = new AtomicJsonStore<SamplePayload>(filePath);
        var backupValue = new SamplePayload("Backup", 1);
        store.Write(backupValue);
        store.Write(new SamplePayload("Current", 2));
        File.WriteAllText(filePath, "{");

        var result = store.Read(new SamplePayload("Default", 0));

        Assert.Equal(backupValue, result.Value);
        Assert.True(result.RecoveredFromBackup);
        Assert.True(result.HadInvalidData);
    }

    [Fact]
    public void Read_whenPrimaryAndBackupAreCorrupt_returnsDefaultAndReportsInvalidData()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var filePath = temporaryDirectory.GetPath("state.json");
        var defaultValue = new SamplePayload("Default", 0);
        File.WriteAllText(filePath, "{");
        File.WriteAllText($"{filePath}.bak", "[");
        var store = new AtomicJsonStore<SamplePayload>(filePath);

        var result = store.Read(defaultValue);

        Assert.Equal(defaultValue, result.Value);
        Assert.False(result.RecoveredFromBackup);
        Assert.True(result.HadInvalidData);
    }

    [Fact]
    public void Write_removesTemporaryFileAfterSaving()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var filePath = temporaryDirectory.GetPath("state.json");
        var store = new AtomicJsonStore<SamplePayload>(filePath);

        store.Write(new SamplePayload("Saved", 1));

        Assert.False(File.Exists($"{filePath}.tmp"));
    }

    [Fact]
    public void Write_andRead_preserveChineseAndEmojiAsUtf8()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var filePath = temporaryDirectory.GetPath("state.json");
        var expected = new SamplePayload("完成買牛奶 😀", 7);
        var store = new AtomicJsonStore<SamplePayload>(filePath);

        store.Write(expected);
        var result = store.Read(new SamplePayload("Default", 0));

        Assert.Equal(expected, result.Value);
        var bytes = File.ReadAllBytes(filePath);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
    }

    private sealed record SamplePayload(string Text, int Version);

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
