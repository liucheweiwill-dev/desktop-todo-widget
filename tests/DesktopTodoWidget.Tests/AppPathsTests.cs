using System.IO;
using DesktopTodoWidget.Data;
using Xunit;

namespace DesktopTodoWidget.Tests;

public sealed class AppPathsTests
{
    [Fact]
    public void Resolve_withoutPortableMarker_usesLocalAppDataDirectory()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var executableDirectory = temporaryDirectory.CreateDirectory("exe");
        var localAppDataDirectory = temporaryDirectory.GetPath("local-app-data");

        var result = new AppPaths(executableDirectory, localAppDataDirectory).Resolve();

        Assert.Equal(Path.Combine(localAppDataDirectory, "DesktopTodoWidget"), result.DataDirectory);
        Assert.False(result.IsPortableStorage);
        Assert.False(result.PortableFallbackOccurred);
    }

    [Fact]
    public void Resolve_withPortableMarker_usesExecutableDirectory()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var executableDirectory = temporaryDirectory.CreateDirectory("exe");
        File.WriteAllText(Path.Combine(executableDirectory, "portable.marker"), string.Empty);

        var result = new AppPaths(executableDirectory, temporaryDirectory.GetPath("local-app-data")).Resolve();

        Assert.Equal(executableDirectory, result.DataDirectory);
        Assert.True(result.IsPortableStorage);
        Assert.False(result.PortableFallbackOccurred);
    }

    [Fact]
    public void Resolve_createsMissingDataDirectory()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var executableDirectory = temporaryDirectory.CreateDirectory("exe");
        var localAppDataDirectory = temporaryDirectory.GetPath("local-app-data");
        var expectedDirectory = Path.Combine(localAppDataDirectory, "DesktopTodoWidget");
        Assert.False(Directory.Exists(expectedDirectory));

        var result = new AppPaths(executableDirectory, localAppDataDirectory).Resolve();

        Assert.Equal(expectedDirectory, result.DataDirectory);
        Assert.True(Directory.Exists(expectedDirectory));
    }

    [Fact]
    public void Resolve_whenPortableDirectoryIsNotWritable_fallsBackAndReportsIt()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var executableDirectory = temporaryDirectory.CreateDirectory("exe");
        var localAppDataDirectory = temporaryDirectory.GetPath("local-app-data");
        File.WriteAllText(Path.Combine(executableDirectory, "portable.marker"), string.Empty);

        var result = new AppPaths(
            executableDirectory,
            localAppDataDirectory,
            portableDirectoryIsWritable: _ => false).Resolve();

        Assert.Equal(Path.Combine(localAppDataDirectory, "DesktopTodoWidget"), result.DataDirectory);
        Assert.False(result.IsPortableStorage);
        Assert.True(result.PortableFallbackOccurred);
        Assert.NotNull(result.PortableFallbackReason);
        Assert.True(Directory.Exists(result.DataDirectory));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Root = Path.Combine(Path.GetTempPath(), $"DesktopTodoWidget.Tests.{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string CreateDirectory(string name)
        {
            var path = GetPath(name);
            Directory.CreateDirectory(path);
            return path;
        }

        public string GetPath(string name) => Path.Combine(Root, name);

        public void Dispose()
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
