#nullable enable
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using ImGuiNET;
using SilkWindows;
using T3.Core.Resource;
using T3.Editor.App;
using T3.Editor.Gui;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.UiContentDrawing;

internal static class UiContentUpdate
{
    public static void SetupResourcesAndFontsWithScaling()
    {
        if (_hasSetScaling && Math.Abs(UserSettings.Config.UiScaleFactor - _lastUiScale) <= 0.005f)
            return;

        Log.Debug("Setup editor resources...");
        // Prevent scale factor from being "actually" 0.0
        if (UserSettings.Config.UiScaleFactor < 0.1f)
            UserSettings.Config.UiScaleFactor = 0.1f;

        // Update font atlas texture if UI-Scale changed
        GenerateFontsWithScaleFactor(UserSettings.Config.UiScaleFactor);

        Program.UiContentContentDrawer?.CreateDeviceObjectsAndFonts();

        _lastUiScale = UserSettings.Config.UiScaleFactor;
        _hasSetScaling = true;
    }

    private static void GenerateFontsWithScaleFactor(float scaleFactor)
    {
        // See https://stackoverflow.com/a/5977638
        var displayScaleFactor = ProgramWindows.Main.GetDpi().X / 96f;
        var dpiAwareScale = scaleFactor * displayScaleFactor;

        T3Ui.UiScaleFactor = dpiAwareScale;

        var fontAtlasPtr = ImGui.GetIO().Fonts;
        fontAtlasPtr.Clear();
        const string fontName = "Inter";
        var rootFilePath = Path.Combine(SharedResources.EditorResourcesDirectory, SharedResources.EditorResourcesDirectory, "fonts",  fontName + '-');

        const string fileExtension = ".ttf";
        var format = $"{rootFilePath}{{0}}{fileExtension}";

        var normalFont = new TtfFont(string.Format(format, "Regular"), 18f * dpiAwareScale);
        var boldFont = new TtfFont(string.Format(format, "SemiBold"), 18f * dpiAwareScale);
        var smallFont = new TtfFont(string.Format(format, "Regular"), 14f * dpiAwareScale);
        var largeFont = new TtfFont(string.Format(format, "Light"), 30f * dpiAwareScale);

        var ranges = GetExtendedGlyphRanges();
        Fonts.FontNormal = fontAtlasPtr.AddFontFromFileTTF(normalFont.Path, normalFont.PixelSize, default, ranges);
        Fonts.FontBold = fontAtlasPtr.AddFontFromFileTTF(boldFont.Path, boldFont.PixelSize, default, ranges);
        Fonts.FontSmall = fontAtlasPtr.AddFontFromFileTTF(smallFont.Path, smallFont.PixelSize, default, ranges);
        Fonts.FontLarge = fontAtlasPtr.AddFontFromFileTTF(largeFont.Path, largeFont.PixelSize, default, ranges);

        var codeFontPath = Path.Combine(SharedResources.EditorResourcesDirectory, SharedResources.EditorResourcesDirectory,"fonts", "JetBrainsMono-Regular.ttf");
        var codeFont = new TtfFont(codeFontPath, 18f * dpiAwareScale);
        Fonts.Code = fontAtlasPtr.AddFontFromFileTTF(codeFont.Path, codeFont.PixelSize, default, ranges);

        ImGuiWindowService.Instance.SetFonts(new FontPack(normalFont, boldFont, smallFont, largeFont));
    }

    /// <summary>
    /// Extended glyph ranges for the editor's TTF fonts. Default ImGui only
    /// rasterizes 0x0020-0x00FF (basic Latin); we additionally pull in common
    /// punctuation used by the markdown renderer (bullet •, dashes – —, smart
    /// quotes ' ' " ", ellipsis …), arrows (→ ← ↑ ↓), miscellaneous symbols
    /// (warning ⚠, gear ⚙), and dingbats (check ✓, cross ✗).
    ///
    /// The returned pointer references a static, GC-pinned array — ImGui only
    /// reads from it during atlas Build(), so the pin can live for the
    /// process lifetime.
    /// </summary>
    private static IntPtr GetExtendedGlyphRanges()
    {
        if (_glyphRangesPtr != IntPtr.Zero)
            return _glyphRangesPtr;

        // Pairs of (first, last), terminated with 0.
        _extendedGlyphRanges = new ushort[]
                                   {
                                       0x0020, 0x00FF,   // Basic Latin + Latin Supplement
                                       0x2010, 0x205E,   // General punctuation
                                       0x2190, // ←
                                       0x21FF, // ⇿  
                                       0x2192, // → Right arrow
                                       0x2600, 0x26FF,   // Miscellaneous symbols (⚠ ⚙ ☆ …)
                                       0x2700, 0x27BF,   // Dingbats (✓ ✗ ✱ …)
                                       0,
                                   };
        _glyphRangesHandle = GCHandle.Alloc(_extendedGlyphRanges, GCHandleType.Pinned);
        _glyphRangesPtr = _glyphRangesHandle.AddrOfPinnedObject();
        return _glyphRangesPtr;
    }

    private static ushort[]? _extendedGlyphRanges;
    private static GCHandle _glyphRangesHandle;
    private static IntPtr _glyphRangesPtr;

    private static long _lastElapsedTicks;
    private static readonly Stopwatch _stopwatch = new();

    private static float _lastUiScale = 1;

    private static bool _hasSetScaling;

    public static void StartMeasureFrame()
    {
        _stopwatch.Start();
        _lastElapsedTicks = _stopwatch.ElapsedTicks;
    }

    public static void TakeMeasurement()
    {
        Int64 ticks = _stopwatch.ElapsedTicks;
        Int64 ticksDiff = ticks - _lastElapsedTicks;
        _lastElapsedTicks = ticks;
        ImGui.GetIO().DeltaTime = (float)((double)(ticksDiff) / Stopwatch.Frequency);
    }
}