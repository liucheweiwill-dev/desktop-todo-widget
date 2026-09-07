using System.IO;
using System.Text.Json;

namespace DesktopTodoWidget.Data;

public sealed class AtomicJsonStore<T>
{
    private readonly string _filePath;
    private readonly string _backupFilePath;
    private readonly string _temporaryFilePath;

    public AtomicJsonStore(string filePath)
    {
        _filePath = string.IsNullOrWhiteSpace(filePath)
            ? throw new ArgumentException("A storage file path is required.", nameof(filePath))
            : filePath;
        _backupFilePath = $"{_filePath}.bak";
        _temporaryFilePath = $"{_filePath}.tmp";
    }

    public void Write(T value)
    {
        try
        {
            using (var temporaryFile = new FileStream(
                       _temporaryFilePath,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None))
            {
                JsonSerializer.Serialize(temporaryFile, value);
                temporaryFile.Flush(flushToDisk: true);
            }

            if (File.Exists(_filePath))
            {
                File.Replace(_temporaryFilePath, _filePath, _backupFilePath);
            }
            else
            {
                File.Move(_temporaryFilePath, _filePath);
            }
        }
        finally
        {
            if (File.Exists(_temporaryFilePath))
            {
                File.Delete(_temporaryFilePath);
            }
        }
    }

    public AtomicJsonReadResult<T> Read(T defaultValue)
    {
        if (!File.Exists(_filePath))
        {
            return new AtomicJsonReadResult<T>(
                defaultValue,
                RecoveredFromBackup: false,
                HadInvalidData: false);
        }

        if (TryRead(_filePath, out var primaryValue))
        {
            return new AtomicJsonReadResult<T>(
                primaryValue,
                RecoveredFromBackup: false,
                HadInvalidData: false);
        }

        if (TryRead(_backupFilePath, out var backupValue))
        {
            return new AtomicJsonReadResult<T>(
                backupValue,
                RecoveredFromBackup: true,
                HadInvalidData: true);
        }

        return new AtomicJsonReadResult<T>(
            defaultValue,
            RecoveredFromBackup: false,
            HadInvalidData: true);
    }

    private static bool TryRead(string filePath, out T value)
    {
        try
        {
            using var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var deserializedValue = JsonSerializer.Deserialize<T>(file);
            if (deserializedValue is null)
            {
                value = default!;
                return false;
            }

            value = deserializedValue;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            value = default!;
            return false;
        }
    }
}

public sealed record AtomicJsonReadResult<T>(
    T Value,
    bool RecoveredFromBackup,
    bool HadInvalidData);
