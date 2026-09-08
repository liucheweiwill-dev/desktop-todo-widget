using System.Text.Json.Serialization;

namespace DesktopTodoWidget.Data;

public sealed class WindowPlacement
{
    public const int CurrentSchemaVersion = 1;

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    [JsonPropertyName("left")]
    public double Left { get; set; }

    [JsonPropertyName("top")]
    public double Top { get; set; }

    public static WindowRectangle ClampToVisibleArea(
        WindowRectangle savedBounds,
        IReadOnlyList<WindowRectangle> workingAreas,
        WindowRectangle defaultBounds)
    {
        ArgumentNullException.ThrowIfNull(workingAreas);

        if (workingAreas.Count == 0 || !IsFinite(savedBounds))
        {
            return defaultBounds;
        }

        foreach (var workingArea in workingAreas)
        {
            if (HasSufficientOverlap(savedBounds, workingArea))
            {
                return savedBounds;
            }
        }

        return ClampToWorkingArea(savedBounds, workingAreas[0]);
    }

    private static bool HasSufficientOverlap(WindowRectangle windowBounds, WindowRectangle workingArea)
    {
        const double minimumVisibleWidth = 100;
        const double minimumVisibleHeight = 30;

        var overlapWidth = Math.Max(
            0,
            Math.Min(windowBounds.Right, workingArea.Right) - Math.Max(windowBounds.Left, workingArea.Left));
        var overlapHeight = Math.Max(
            0,
            Math.Min(windowBounds.Bottom, workingArea.Bottom) - Math.Max(windowBounds.Top, workingArea.Top));

        return overlapWidth >= minimumVisibleWidth && overlapHeight >= minimumVisibleHeight;
    }

    private static WindowRectangle ClampToWorkingArea(WindowRectangle windowBounds, WindowRectangle workingArea)
    {
        var left = windowBounds.Width >= workingArea.Width
            ? workingArea.Left
            : Math.Clamp(windowBounds.Left, workingArea.Left, workingArea.Right - windowBounds.Width);
        var top = windowBounds.Height >= workingArea.Height
            ? workingArea.Top
            : Math.Clamp(windowBounds.Top, workingArea.Top, workingArea.Bottom - windowBounds.Height);

        return windowBounds with { Left = left, Top = top };
    }

    private static bool IsFinite(WindowRectangle bounds) =>
        double.IsFinite(bounds.Left) &&
        double.IsFinite(bounds.Top) &&
        double.IsFinite(bounds.Width) &&
        double.IsFinite(bounds.Height);
}

public readonly record struct WindowRectangle(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;

    public double Bottom => Top + Height;
}
