using SDL;
using T3.SystemUi;
using static SDL.SDL3;

namespace T3.SdlPlatform;

/// <summary>
/// Lists displays through SDL, so windows can be placed and made fullscreen on the display a
/// <see cref="DisplayInfo"/> describes. Requires SDL's video subsystem.
/// </summary>
public sealed unsafe class SdlDisplayProvider : IDisplayProvider
{
    public IReadOnlyList<DisplayInfo> GetDisplays()
    {
        _displayIds.Clear();
        var result = new List<DisplayInfo>();
        var primaryId = SDL_GetPrimaryDisplay();

        int displayCount;
        var displayIds = SDL_GetDisplays(&displayCount);
        if (displayIds == null)
            return result;

        for (var index = 0; index < displayCount; index++)
        {
            var displayId = displayIds[index];
            _displayIds.Add(displayId);

            SDL_Rect bounds;
            SDL_GetDisplayBounds(displayId, &bounds);

            var desktopMode = SDL_GetDesktopDisplayMode(displayId);
            var current = desktopMode != null
                              ? ToDisplayMode(desktopMode)
                              : new DisplayMode(bounds.w, bounds.h, 0);

            result.Add(new DisplayInfo(index,
                                       SDL_GetDisplayName(displayId) ?? $"Display {index + 1}",
                                       new System.Drawing.Rectangle(bounds.x, bounds.y, bounds.w, bounds.h),
                                       displayId == primaryId,
                                       current,
                                       GetModes(displayId, current)));
        }

        SDL_free(displayIds);
        return result;
    }

    /// <summary>The SDL display behind an index from the last <see cref="GetDisplays"/> call.</summary>
    public SDL_DisplayID GetDisplayId(int displayIndex)
    {
        return displayIndex >= 0 && displayIndex < _displayIds.Count ? _displayIds[displayIndex] : SDL_GetPrimaryDisplay();
    }

    /// <summary>One entry per resolution with its highest refresh rate, largest first.</summary>
    private static List<DisplayMode> GetModes(SDL_DisplayID displayId, DisplayMode current)
    {
        var modesByResolution = new Dictionary<(int, int), DisplayMode>();
        int modeCount;
        var modes = SDL_GetFullscreenDisplayModes(displayId, &modeCount);
        if (modes != null)
        {
            for (var index = 0; index < modeCount; index++)
            {
                var mode = ToDisplayMode(modes[index]);
                var resolution = (mode.Width, mode.Height);
                if (!modesByResolution.TryGetValue(resolution, out var existing) || existing.RefreshRate < mode.RefreshRate)
                    modesByResolution[resolution] = mode;
            }

            SDL_free(modes);
        }

        // Wayland exposes no mode list; the desktop mode is the one that is always there.
        modesByResolution.TryAdd((current.Width, current.Height), current);

        return modesByResolution.Values
                                .OrderByDescending(mode => mode.Width * mode.Height)
                                .ThenByDescending(mode => mode.Width)
                                .ToList();
    }

    /// <summary>SDL reports modes in points; the pixel density turns them into pixels.</summary>
    private static DisplayMode ToDisplayMode(SDL_DisplayMode* mode)
    {
        var density = mode->pixel_density > 0 ? mode->pixel_density : 1f;
        return new DisplayMode((int)MathF.Round(mode->w * density),
                               (int)MathF.Round(mode->h * density),
                               (int)MathF.Round(mode->refresh_rate));
    }

    private readonly List<SDL_DisplayID> _displayIds = [];
}
