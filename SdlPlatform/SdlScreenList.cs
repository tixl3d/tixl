using System.Drawing;
using SDL;
using T3.SystemUi;
using static SDL.SDL3;

namespace T3.SdlPlatform;

/// <summary>
/// The displays as <see cref="IScreen"/>s, in <see cref="SdlDisplayProvider"/>'s order, so an index means the same
/// display to both. Read once and kept: callers ask every frame, and reading allocates.
/// </summary>
/// <remarks>
/// A bound's position is in SDL's desktop coordinates and its size in pixels — the display mode, which is what
/// callers treat as the resolution. On Windows the two agree; on a scaled Wayland desktop, the positions are
/// in points.
/// </remarks>
public sealed unsafe class SdlScreenList
{
    /// <summary>A new list after <see cref="Invalidate"/>, the same instance otherwise.</summary>
    public IReadOnlyList<IScreen> Screens => _screens ??= Read();

    /// <summary>Call when SDL reports a display added, removed, moved or changed.</summary>
    public void Invalidate() => _screens = null;

    private IReadOnlyList<IScreen> Read()
    {
        var displays = _displayProvider.GetDisplays();
        var screens = new List<IScreen>(displays.Count);
        for (var index = 0; index < displays.Count; index++)
        {
            var display = displays[index];
            var size = new Size(display.CurrentMode.Width, display.CurrentMode.Height);

            SDL_Rect usable;
            var workingArea = SDL_GetDisplayUsableBounds(_displayProvider.GetDisplayId(index), &usable)
                                  ? new Rectangle(usable.x, usable.y, usable.w, usable.h)
                                  : display.Bounds;

            screens.Add(new Screen(new Rectangle(display.Bounds.Location, size), workingArea, display.Name, display.IsPrimary));
        }

        return screens;
    }

    private sealed record Screen(Rectangle Bounds, Rectangle WorkingArea, string DeviceName, bool Primary) : IScreen
    {
        public int BitsPerPixel => 32;
    }

    private readonly SdlDisplayProvider _displayProvider = new();
    private IReadOnlyList<IScreen>? _screens;
}
