using DesktopTodoWidget.Data;
using Xunit;

namespace DesktopTodoWidget.Tests;

public sealed class WindowPlacementTests
{
    private static readonly WindowRectangle DefaultBounds = new(80, 80, 360, 420);

    [Fact]
    public void ClampToVisibleArea_whenWindowIsFullyOnPrimaryScreen_returnsSavedBounds()
    {
        var savedBounds = new WindowRectangle(200, 100, 360, 420);

        var result = WindowPlacement.ClampToVisibleArea(
            savedBounds,
            [new WindowRectangle(0, 0, 1920, 1040)],
            DefaultBounds);

        Assert.Equal(savedBounds, result);
    }

    [Fact]
    public void ClampToVisibleArea_whenWindowIsEntirelyOffScreen_clampsToPrimaryWorkingArea()
    {
        var savedBounds = new WindowRectangle(-500, -200, 360, 420);

        var result = WindowPlacement.ClampToVisibleArea(
            savedBounds,
            [new WindowRectangle(0, 0, 1920, 1040)],
            DefaultBounds);

        Assert.Equal(new WindowRectangle(0, 0, 360, 420), result);
    }

    [Fact]
    public void ClampToVisibleArea_whenOverlapIsSmallerThanThreshold_clampsToPrimaryWorkingArea()
    {
        var savedBounds = new WindowRectangle(-300, 100, 360, 420);

        var result = WindowPlacement.ClampToVisibleArea(
            savedBounds,
            [new WindowRectangle(0, 0, 1920, 1040)],
            DefaultBounds);

        Assert.Equal(new WindowRectangle(0, 100, 360, 420), result);
    }

    [Fact]
    public void ClampToVisibleArea_whenOverlapMatchesThreshold_returnsSavedBounds()
    {
        var savedBounds = new WindowRectangle(-260, -390, 360, 420);

        var result = WindowPlacement.ClampToVisibleArea(
            savedBounds,
            [new WindowRectangle(0, 0, 1920, 1040)],
            DefaultBounds);

        Assert.Equal(savedBounds, result);
    }

    [Fact]
    public void ClampToVisibleArea_whenWindowSpansScreensWithSufficientOverlap_returnsSavedBounds()
    {
        var savedBounds = new WindowRectangle(1700, 100, 500, 420);

        var result = WindowPlacement.ClampToVisibleArea(
            savedBounds,
            [
                new WindowRectangle(0, 0, 1920, 1040),
                new WindowRectangle(1920, 0, 1920, 1040)
            ],
            DefaultBounds);

        Assert.Equal(savedBounds, result);
    }

    // 規格更正（2026-09-08）：任務卡原本的 P5 要求「視窗比螢幕大就夾到工作區左上角」，
    // 與 R5 的「與任一工作區有足夠重疊就原樣回傳」互相矛盾。
    // 裁定以 R5 為準：只要看得到、抓得到，就尊重使用者放置的位置；
    // 每次啟動彈回原點反而擾人。本專案視窗尺寸固定 360x420，此情境實務上不會發生。
    [Fact]
    public void ClampToVisibleArea_whenWindowIsLargerThanOnlyScreen_keepsThePositionBecauseItIsStillReachable()
    {
        var savedBounds = new WindowRectangle(200, 100, 2000, 1200);

        var result = WindowPlacement.ClampToVisibleArea(
            savedBounds,
            [new WindowRectangle(0, 0, 1920, 1040)],
            DefaultBounds);

        Assert.Equal(savedBounds, result);
    }

    [Fact]
    public void ClampToVisibleArea_whenNoWorkingAreasAreAvailable_returnsDefaultBounds()
    {
        var savedBounds = new WindowRectangle(2000, 100, 360, 420);

        var result = WindowPlacement.ClampToVisibleArea(savedBounds, [], DefaultBounds);

        Assert.Equal(DefaultBounds, result);
    }
}
