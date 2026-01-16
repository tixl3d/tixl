#nullable enable
using System.Diagnostics;
using System.IO;
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
        T3Ui.DisplayScaleFactor = ProgramWindows.Main.GetDpi().X / 96f;
        var dpiAwareScale = scaleFactor * T3Ui.DisplayScaleFactor;

        T3Ui.UiScaleFactor = dpiAwareScale;

        var fontAtlasPtr = ImGui.GetIO().Fonts;
        fontAtlasPtr.Clear();
        const string fontName = "Roboto";
        var rootFilePath = Path.Combine(SharedResources.Directory, "fonts", "editor", fontName + '-');

        const string fileExtension = ".ttf";
        var format = $"{rootFilePath}{{0}}{fileExtension}";

        var normalFont = new TtfFont(string.Format(format, "Regular"), 18f * dpiAwareScale);
        var boldFont = new TtfFont(string.Format(format, "Medium"), 18f * dpiAwareScale);
        var smallFont = new TtfFont(string.Format(format, "Regular"), 14f * dpiAwareScale);
        var largeFont = new TtfFont(string.Format(format, "Light"), 30f * dpiAwareScale);

        Fonts.FontNormal = fontAtlasPtr.AddFontFromFileTTF(normalFont.Path, normalFont.PixelSize);
        Fonts.FontBold = fontAtlasPtr.AddFontFromFileTTF(boldFont.Path, boldFont.PixelSize);
        Fonts.FontSmall = fontAtlasPtr.AddFontFromFileTTF(smallFont.Path, smallFont.PixelSize);
        Fonts.FontLarge = fontAtlasPtr.AddFontFromFileTTF(largeFont.Path, largeFont.PixelSize);

        var codeFontPath = Path.Combine(SharedResources.Directory, "fonts", "editor", "JetBrainsMono-Regular.ttf");
        var codeFont = new TtfFont(codeFontPath, 18f * dpiAwareScale);
        Fonts.Code = fontAtlasPtr.AddFontFromFileTTF(codeFont.Path, codeFont.PixelSize);

        ImGuiWindowService.Instance.SetFonts(new FontPack(normalFont, boldFont, smallFont, largeFont));
    }

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