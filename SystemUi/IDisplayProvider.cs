using System.Drawing;

namespace T3.SystemUi;

/// <summary>
/// A resolution supported by a display. <see cref="RefreshRate"/> is 0 when unknown.
/// </summary>
public readonly record struct DisplayMode(int Width, int Height, int RefreshRate)
{
    public override string ToString() => RefreshRate > 0 ? $"{Width} x {Height} @ {RefreshRate} Hz" : $"{Width} x {Height}";
}

/// <summary>
/// A connected display. <see cref="Bounds"/> is in virtual-desktop pixels so it can be matched against
/// platform screen APIs; <see cref="Modes"/> lists distinct resolutions, highest first.
/// </summary>
public sealed record DisplayInfo(int Index, string Name, Rectangle Bounds, bool IsPrimary, DisplayMode CurrentMode, IReadOnlyList<DisplayMode> Modes)
{
    public override string ToString() => $"{Index + 1}: {Name} ({CurrentMode.Width} x {CurrentMode.Height}){(IsPrimary ? " [primary]" : "")}";
}

public interface IDisplayProvider
{
    /// <summary>Enumerates displays in platform order; index 0 is not necessarily the primary display.</summary>
    IReadOnlyList<DisplayInfo> GetDisplays();
}
