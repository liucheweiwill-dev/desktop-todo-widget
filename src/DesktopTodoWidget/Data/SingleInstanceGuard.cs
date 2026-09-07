using System.Threading;

namespace DesktopTodoWidget.Data;

public sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexNamePrefix = @"Local\DesktopTodoWidget.";
    private Mutex? _mutex;

    private SingleInstanceGuard(Mutex mutex)
    {
        _mutex = mutex;
    }

    public static SingleInstanceGuard? TryAcquire(string instanceName)
    {
        if (string.IsNullOrWhiteSpace(instanceName))
        {
            throw new ArgumentException("An instance name is required.", nameof(instanceName));
        }

        Mutex? mutex = null;
        try
        {
            mutex = new Mutex(
                initiallyOwned: true,
                name: $"{MutexNamePrefix}{instanceName}",
                createdNew: out var createdNew);

            if (!createdNew)
            {
                mutex.Dispose();
                return null;
            }

            return new SingleInstanceGuard(mutex);
        }
        catch (UnauthorizedAccessException)
        {
            mutex?.Dispose();
            return null;
        }
    }

    public void Dispose()
    {
        var mutex = Interlocked.Exchange(ref _mutex, null);
        if (mutex is null)
        {
            return;
        }

        try
        {
            mutex.ReleaseMutex();
        }
        finally
        {
            mutex.Dispose();
        }
    }

    // No mutex ACL is configured: another user on the same machine can interfere with this name.
    // The application is single-user, so the documented D4 limitation is intentionally accepted.
}
