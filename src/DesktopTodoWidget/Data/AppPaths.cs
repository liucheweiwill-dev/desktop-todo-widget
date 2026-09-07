using System.IO;

namespace DesktopTodoWidget.Data;

public sealed class AppPaths
{
    private const string ApplicationDirectoryName = "DesktopTodoWidget";
    private const string PortableMarkerFileName = "portable.marker";

    private readonly string _executableDirectory;
    private readonly string _localAppDataDirectory;
    private readonly Func<string, bool>? _portableDirectoryIsWritable;

    public AppPaths()
        : this(
            AppContext.BaseDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))
    {
    }

    public AppPaths(
        string executableDirectory,
        string localAppDataDirectory,
        Func<string, bool>? portableDirectoryIsWritable = null)
    {
        _executableDirectory = string.IsNullOrWhiteSpace(executableDirectory)
            ? throw new ArgumentException("An executable directory is required.", nameof(executableDirectory))
            : executableDirectory;
        _localAppDataDirectory = string.IsNullOrWhiteSpace(localAppDataDirectory)
            ? throw new ArgumentException("A LocalAppData directory is required.", nameof(localAppDataDirectory))
            : localAppDataDirectory;
        _portableDirectoryIsWritable = portableDirectoryIsWritable;
    }

    public AppPathsResult Resolve()
    {
        if (!File.Exists(Path.Combine(_executableDirectory, PortableMarkerFileName)))
        {
            return ResolveDefaultStorage();
        }

        if (IsPortableDirectoryWritable())
        {
            Directory.CreateDirectory(_executableDirectory);
            return new AppPathsResult(
                _executableDirectory,
                IsPortableStorage: true,
                PortableFallbackOccurred: false,
                PortableFallbackReason: null);
        }

        var fallback = ResolveDefaultStorage();
        return fallback with
        {
            PortableFallbackOccurred = true,
            PortableFallbackReason = "Portable storage directory is not writable."
        };
    }

    private AppPathsResult ResolveDefaultStorage()
    {
        var dataDirectory = Path.Combine(_localAppDataDirectory, ApplicationDirectoryName);
        Directory.CreateDirectory(dataDirectory);
        return new AppPathsResult(
            dataDirectory,
            IsPortableStorage: false,
            PortableFallbackOccurred: false,
            PortableFallbackReason: null);
    }

    private bool IsPortableDirectoryWritable()
    {
        if (_portableDirectoryIsWritable is not null)
        {
            return _portableDirectoryIsWritable(_executableDirectory);
        }

        var probeFilePath = Path.Combine(
            _executableDirectory,
            $".{ApplicationDirectoryName}.{Guid.NewGuid():N}.write-probe");

        try
        {
            Directory.CreateDirectory(_executableDirectory);
            using (var probe = new FileStream(probeFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                probe.Flush(flushToDisk: true);
            }

            File.Delete(probeFilePath);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
        finally
        {
            TryDeleteProbeFile(probeFilePath);
        }
    }

    private static void TryDeleteProbeFile(string probeFilePath)
    {
        try
        {
            if (File.Exists(probeFilePath))
            {
                File.Delete(probeFilePath);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A failed cleanup must not prevent portable-mode fallback.
        }
    }
}

public sealed record AppPathsResult(
    string DataDirectory,
    bool IsPortableStorage,
    bool PortableFallbackOccurred,
    string? PortableFallbackReason);
