using ImGuiNET;
using Microsoft.Win32;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using T3.Core.Utils;
using T3.Editor.App;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.Layouts;
using Icon = T3.Editor.Gui.Styling.Icon;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Windows;

internal sealed class ScreenManager : Window
{
    internal ScreenManager()
    {
        Config.Title = "Screen Manager";
        SystemEvents.DisplaySettingsChanged += (_, __) => _layoutDirty = true;
    }

    protected override void DrawContent()
    {
        FormInputs.AddVerticalSpace(15);

        DrawInnerContent();
    }

    internal override IReadOnlyList<Window> GetInstances()
    {
        throw new NotImplementedException();
    }

    private static void DrawInnerContent()
    {
        RefreshScreenCache();
        ImGui.Indent(10);
        FormInputs.AddVerticalSpace(10);

        var windowWidth = ImGui.GetWindowWidth();

        // Mark layout dirty if window width changed
        if (Math.Abs(windowWidth - _lastWindowWidth) > 0.5f)
        {
            _layoutDirty = true;
            _lastWindowWidth = windowWidth;
        }

        if (_layoutDirty)
        {
            _cachedScale = ComputeScale(_cachedOverallBounds, windowWidth);
            _layoutDirty = false;
        }
        ImGui.Unindent(10);
        DrawScreenLayout(_cachedScreens, _cachedScale);

        ImGui.Indent(10);
        FormInputs.AddVerticalSpace(20);

        if (ImGui.Button("Windows Display settings" + new string(' ', 5)))
            OpenWindowsDisplaySettings();

        Icons.DrawIconOnLastItem(Icon.OpenExternally, UiColors.Text, .99f);
        CustomComponents.TooltipForLastItem("Open Windows display settings to configure screen arrangement, resolution, etc.");
        ImGui.Dummy(new Vector2(1, 0));
        ImGui.SameLine(windowWidth - 30);
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        ImGui.PushFont(Icons.IconFont);
        Icons.DrawAtCursor(Icon.Tip);
        ImGui.PopFont();
        ImGui.PopStyleColor();
        CustomComponents.TooltipForLastItem("Press F11 twice to update the UI position");

        FormInputs.AddVerticalSpace(20);
        
        

        ImGui.Unindent(10);

        if (_spanningNeedsUpdate)
        {
            ApplySpanningChanges();
            _spanningNeedsUpdate = false;
        }
    }


    private static void ApplySpanningChanges()
    {
        var spanningBounds = UserSettings.Config.OutputArea;

        // Check if spanning area is defined
        if (spanningBounds.Z > 0 && spanningBounds.W > 0)
        {
            // Enable the output window if not already enabled
            if (!WindowManager.ShowSecondaryRenderWindow)
            {
                WindowManager.ShowSecondaryRenderWindow = true;
            }

            // Update the viewer window with the new spanning area
            // This will be picked up by the main update loop
            ProgramWindows.UpdateViewerSpanning(spanningBounds);
        }
        else
        {
            // No spanning area defined, back to windowed mode
            ProgramWindows.Viewer.SetSizeable();
        }
    }

    private static bool IsScreenInSpanningArea(Screen screen, Vector4 spanningArea)
    {
        if (spanningArea.Z == 0 || spanningArea.W == 0) // No spanning area defined
            return false;

        var screenBounds = screen.Bounds;

        // Check if the screen's bounds are completely within the spanning area
        return screenBounds.Left >= spanningArea.X &&
               screenBounds.Right <= spanningArea.X + spanningArea.Z &&
               screenBounds.Top >= spanningArea.Y &&
               screenBounds.Bottom <= spanningArea.Y + spanningArea.W;
    }

    private static void AddScreenToSpanning(Screen screen, Screen[] screens)
    {
        var currentBounds = UserSettings.Config.OutputArea;
        var screenBounds = screen.Bounds;

        // Get all screens that are currently in the spanning area
        var currentScreens = screens.Where(s => IsScreenInSpanningArea(s, currentBounds)).ToList();

        // Add the new screen
        if (!currentScreens.Contains(screen))
            currentScreens.Add(screen);

        // Calculate new combined bounds
        UpdateSpanningBounds([.. currentScreens]);
    }

    private static void RemoveScreenFromSpanning(Screen screen, Screen[] screens)
    {
        var currentBounds = UserSettings.Config.OutputArea;

        // Get all screens that are currently in the spanning area, excluding the one to remove
        var remainingScreens = screens.Where(s => IsScreenInSpanningArea(s, currentBounds) && s != screen).ToArray();

        // Update bounds with remaining screens
        UpdateSpanningBounds(remainingScreens);
    }

    private static void UpdateSpanningBounds(Screen[] selectedScreens)
    {
        if (selectedScreens.Length == 0)
        {
            UserSettings.Config.OutputArea = new Vector4(0, 0, 0, 0);
            return;
        }

        var minX = selectedScreens.Min(s => s.Bounds.X);
        var minY = selectedScreens.Min(s => s.Bounds.Y);
        var maxX = selectedScreens.Max(s => s.Bounds.Right);
        var maxY = selectedScreens.Max(s => s.Bounds.Bottom);

        UserSettings.Config.OutputArea = new Vector4(
            minX, minY,
            maxX - minX, maxY - minY
        );
    }

    private static Rectangle GetOverallScreenBounds(Screen[] screens)
    {
        if (screens.Length == 0)
            return new Rectangle(0, 0, 0, 0);

        var minX = screens.Min(s => s.Bounds.X);
        var minY = screens.Min(s => s.Bounds.Y);
        var maxX = screens.Max(s => s.Bounds.Right);
        var maxY = screens.Max(s => s.Bounds.Bottom);

        return new Rectangle(minX, minY, maxX - minX, maxY - minY);
    }

    private static void OpenWindowsDisplaySettings()
    {
        try
        {
            // Modern Windows 10/11 way - opens directly to display settings
            Process.Start(new ProcessStartInfo
            {
                FileName = "ms-settings:display",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            // Fallback methods if the modern way fails
            try
            {
                // Alternative method 1 - Control panel display settings
                Process.Start("control", "desk.cpl,,3");
            }
            catch
            {
                // Alternative method 2 - Direct display properties
                try
                {
                    Process.Start("desk.cpl");
                }
                catch (Exception fallbackEx)
                {
                    // Log the error or show a message to the user
                    Debug.WriteLine($"Failed to open display settings: {ex.Message}");
                    Debug.WriteLine($"Fallback also failed: {fallbackEx.Message}");
                }
            }
        }
    }

    private static bool IsSpanningAreaLargerThanScreens(Screen[] screens, Vector4 spanningArea)
    {
        if (spanningArea.Z == 0 || spanningArea.W == 0)
            return false;

        // Get all screens that are currently in the spanning area
        var screensInSpanning = screens.Where(s => IsScreenInSpanningArea(s, spanningArea)).ToArray();

        if (screensInSpanning.Length == 0)
            return false;

        // Calculate the total area of all screens in the spanning area
        var totalScreenArea = screensInSpanning.Sum(s => s.Bounds.Width * s.Bounds.Height);

        // Calculate the spanning area
        var spanningAreaTotal = spanningArea.Z * spanningArea.W;

        // If spanning area is larger than the sum of screen areas, there are gaps
        return spanningAreaTotal > totalScreenArea;
    }

    private static void RefreshScreenCache()
    {
        var newScreens = Screen.AllScreens;

        // Trigger layout rebuild if number or names differ
        var screensChanged =
            _cachedScreens.Length != newScreens.Length ||
            !_cachedScreens.Select(s => s.DeviceName).SequenceEqual(newScreens.Select(s => s.DeviceName));

        // Trigger rebuild if bounds changed (e.g., moved monitors or changed resolution)
        if (!screensChanged)
        {
            for (var i = 0; i < newScreens.Length; i++)
            {
                if (!_cachedScreens[i].Bounds.Equals(newScreens[i].Bounds))
                {
                    screensChanged = true;
                    break;
                }
            }
        }

        if (screensChanged)
        {
            _cachedScreens = newScreens;
            _cachedOverallBounds = GetOverallScreenBounds(newScreens);
            _layoutDirty = true;
        }

        // Always refresh spanning area, since user may adjust it via settings
        _cachedSpanning = UserSettings.Config.OutputArea;

        // Refresh selected spanning screens
        _screensInSpanning.Clear();
        foreach (var s in _cachedScreens)
            if (IsScreenInSpanningArea(s, _cachedSpanning))
                _screensInSpanning.Add(s);
    }


    // -------------------------------------------
    // Helper: Compute scale to fit screens in UI
    // -------------------------------------------
    private static float ComputeScale(Rectangle overallBounds, float windowWidth)
    {
        var baseScale = 0.1f * T3Ui.UiScaleFactor;
        var horizontalMargin = 20f;
        var availableWidth = windowWidth - (horizontalMargin * 2);

        var neededWidth = overallBounds.Width * baseScale;
        var scaleFactorX = availableWidth / neededWidth;

        //return baseScale * MathF.Min(1, scaleFactorX);

        var finalScale = baseScale * MathF.Min(1, scaleFactorX);
        finalScale = Math.Clamp(finalScale, .6f * baseScale, 2.0f);

        return finalScale;
    }

    // -------------------------------------------
    // DrawScreenLayout() - optimized for low CPU
    // -------------------------------------------
    private static void DrawScreenLayout(Screen[] screens, float scale)
    {
        var drawList = ImGui.GetWindowDrawList();
        var canvasPos = ImGui.GetCursorScreenPos();
        var windowWidth = ImGui.GetWindowWidth();
        var horizontalMargin = 10f;

        var overall = _cachedOverallBounds;
        var neededArea = new Vector2(overall.Width, overall.Height) * scale;
        var centerOffsetX = MathF.Max(horizontalMargin, (windowWidth - neededArea.X) * 0.5f);

        ImGui.InvisibleButton("screen_layout_canvas", neededArea);

        // Draw each screen rectangle
        for (var i = 0; i < screens.Length; i++)
        {
            var screen = screens[i];
            var b = screen.Bounds;
            var x = canvasPos.X + centerOffsetX + (b.X - overall.X) * scale;
            var y = canvasPos.Y + (b.Y - overall.Y) * scale;
            var w = b.Width * scale;
            var h = b.Height * scale;
            var min = new Vector2(x, y);
            var max = new Vector2(x + w, y + h);

            var color = UiColors.BackgroundButton.Rgba;
            drawList.AddRectFilled(min, max, ImGui.ColorConvertFloat4ToU32(color));
            drawList.AddRect(min, max, ImGui.ColorConvertFloat4ToU32(Vector4.UnitW));

            // Label and resolution
            var label = $"{i + 1}" + (screen.Primary ? " (Primary)" : "");
            var resolution = $"{b.Width}x{b.Height}";
            var labelPos = new Vector2(x + w * 0.5f - ImGui.CalcTextSize(label).X / 2, y + h * 0.23f);
            drawList.AddText(labelPos, UiColors.Text, label);

            ImGui.PushFont(Fonts.FontSmall);
            var resPos = new Vector2(x + w * 0.5f - ImGui.CalcTextSize(resolution).X / 2, labelPos.Y + 19 * T3Ui.UiScaleFactor);
            drawList.AddText(resPos, UiColors.TextMuted, resolution);
            ImGui.PopFont();
        }

        // Draw spanning overlay if valid
        if (_cachedSpanning.Z > 0 && _cachedSpanning.W > 0 && _screensInSpanning.Count > 1)
        {
            var hasGaps = IsSpanningAreaLargerThanScreens(screens, _cachedSpanning);

            var rectMin = new Vector2(
                canvasPos.X + centerOffsetX + (_cachedSpanning.X - overall.X) * scale,
                canvasPos.Y + (_cachedSpanning.Y - overall.Y) * scale
            );
            var rectMax = rectMin + new Vector2(_cachedSpanning.Z * scale, _cachedSpanning.W * scale);

            drawList.AddRect(rectMin, rectMax, UiColors.BackgroundActive.Fade(0.5f), 0, ImDrawFlags.RoundCornersNone, 2);

            var labelHeight = 20 * T3Ui.UiScaleFactor;
            var labelRectMin = new Vector2(rectMin.X, rectMax.Y);
            var labelRectMax = new Vector2(rectMax.X, rectMax.Y + labelHeight);
            drawList.AddRectFilled(labelRectMin, labelRectMax, UiColors.BackgroundActive.Fade(0.3f));

            ImGui.PushFont(Fonts.FontSmall);
            var text = hasGaps ? "Spanning (with gaps)" : "Spanning";
            var textSize = ImGui.CalcTextSize(text);
            var textPos = new Vector2(
                labelRectMin.X + ((_cachedSpanning.Z * scale) - textSize.X) * 0.5f,
                labelRectMin.Y + (labelHeight - textSize.Y) * 0.5f
            );
            drawList.AddText(textPos, UiColors.Text, text);
            ImGui.PopFont();
        }

        // -----------------------------
        // Child for interactive buttons
        // -----------------------------
        ImGui.SetCursorScreenPos(canvasPos + new Vector2(centerOffsetX, 0));
        ImGui.BeginChild("Editor screen selection", neededArea, false, ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoScrollbar);
        var buttonSize = new Vector2(16, 16) * T3Ui.UiScaleFactor;

        for (var i = 0; i < screens.Length; i++)
        {
            var screen = screens[i];
            var b = screen.Bounds;
            var x = (b.X - overall.X) * scale;
            var y = (b.Y - overall.Y) * scale;
            var w = b.Width * scale;
            var h = b.Height * scale;

            // Fullscreen (logo) toggle
            ImGui.SetCursorPos(new Vector2(x + w * 0.30f, y + h * 0.75f));
            ImGui.PushID($"screen_radio_{i}");
            var isFullScreen = (UserSettings.Config.FullScreenIndexMain == i);
            if (CustomComponents.ToggleIconButton(ref isFullScreen, Icon.TixlLogo, buttonSize))
                UserSettings.Config.FullScreenIndexMain = i;
            CustomComponents.TooltipForLastItem($"Set Tixl UI on screen {i + 1}");
            ImGui.PopID();

            // Spanning toggle
            ImGui.SetCursorPos(new Vector2(x + w * 0.70f - buttonSize.X, y + h * 0.75f));
            ImGui.PushID($"screen_span_{i}");
            var isSpanning = _screensInSpanning.Contains(screen);
            var previous = isSpanning;
            if (CustomComponents.ToggleIconButton(ref isSpanning, Icon.PlayOutput, buttonSize))
            {
                if (isSpanning && !previous)
                    AddScreenToSpanning(screen, screens);
                else if (!isSpanning && previous)
                    RemoveScreenFromSpanning(screen, screens);

                _spanningNeedsUpdate = true;
            }
            // Display different tooltip based on current spanning state
            if (isSpanning)
            {
                CustomComponents.TooltipForLastItem($"Remove Screen {i + 1} from Output Window");
            }
            else
            {
                CustomComponents.TooltipForLastItem($"Add Screen {i + 1} to Output Window");
            }
            ImGui.PopID();
        }
        
        ImGui.EndChild();
        ImGui.SetCursorScreenPos(canvasPos + new Vector2(0, neededArea.Y));
    }

    private static Screen[] _cachedScreens = [];
    private static Rectangle _cachedOverallBounds;
    private static float _cachedScale;
    private static bool _layoutDirty = true;
    private static bool _spanningNeedsUpdate;
    private static Vector4 _cachedSpanning;
    private static readonly HashSet<Screen> _screensInSpanning = [];
    private static float _lastWindowWidth = -1;
}