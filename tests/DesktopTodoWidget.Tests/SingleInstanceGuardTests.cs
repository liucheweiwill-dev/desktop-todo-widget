using DesktopTodoWidget.Data;
using Xunit;

namespace DesktopTodoWidget.Tests;

public sealed class SingleInstanceGuardTests
{
    [Fact]
    public void TryAcquire_forTheFirstInstance_succeeds()
    {
        var guard = SingleInstanceGuard.TryAcquire(CreateMutexName());

        Assert.NotNull(guard);
        guard!.Dispose();
    }

    [Fact]
    public void TryAcquire_forTheSameMutexName_returnsNullWithoutThrowing()
    {
        var mutexName = CreateMutexName();
        using var firstGuard = SingleInstanceGuard.TryAcquire(mutexName);
        Assert.NotNull(firstGuard);

        var exception = Record.Exception(() => SingleInstanceGuard.TryAcquire(mutexName));

        Assert.Null(exception);
        Assert.Null(SingleInstanceGuard.TryAcquire(mutexName));
    }

    [Fact]
    public void TryAcquire_afterTheFirstGuardIsReleased_succeeds()
    {
        var mutexName = CreateMutexName();
        var firstGuard = SingleInstanceGuard.TryAcquire(mutexName);
        Assert.NotNull(firstGuard);

        firstGuard!.Dispose();
        var secondGuard = SingleInstanceGuard.TryAcquire(mutexName);

        Assert.NotNull(secondGuard);
        secondGuard!.Dispose();
    }

    [Fact]
    public void Dispose_releasesTheMutexForAnotherGuard()
    {
        var mutexName = CreateMutexName();
        using (var guard = SingleInstanceGuard.TryAcquire(mutexName))
        {
            Assert.NotNull(guard);
        }

        var nextGuard = SingleInstanceGuard.TryAcquire(mutexName);

        Assert.NotNull(nextGuard);
        nextGuard!.Dispose();
    }

    private static string CreateMutexName() => $"DesktopTodoWidget.Tests.{Guid.NewGuid():N}";
}
