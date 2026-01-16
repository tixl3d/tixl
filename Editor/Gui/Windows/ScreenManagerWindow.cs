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
internal sealed class ScreenManagerWindow : Window
{
    internal ScreenManagerWindow()
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
        FormInputs.AddVerticalSpace();

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

        if (ImGui.Button("Windows Display settings    "))
        {
            OpenWindowsDisplaySettings();
        }    
        Icons.DrawIconOnLastItem(Icon.OpenExternally, UiColors.Text, .99f);
        CustomComponents.TooltipForLastItem("Open Windows display settings to configure screen arrangement, resolution, etc.");

        ImGui.SetCursorPosX(windowWidth - 30);
        Icon.Tip.DrawAtCursor();
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
        var spanning = UserSettings.Config.OutputArea;
        var spanningBounds = new ImRect(new Vector2(spanning.X, spanning.Y), new Vector2(spanning.Z, spanning.W));

        var isSpanningDefined = spanningBounds.Max.X > 0 && spanningBounds.Max.Y > 0;
        if (isSpanningDefined)
        {
            // Enable the output window if not already enabled
            
            WindowManager.ShowSecondaryRenderWindow = true;
           
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

    private static bool IsScreenInSpanningArea(Screen screen, ImRect spanningArea)
    {
        var undefinedSpanning = spanningArea.Max.X == 0 || spanningArea.Max.X == 0;
        if (undefinedSpanning)
            return false;

        var screenBounds = screen.Bounds;

        // Check if the screen's bounds are completely within the spanning area
        return screenBounds.Left >= spanningArea.Min.X &&
               screenBounds.Right <= spanningArea.Min.X + spanningArea.Max.X &&
               screenBounds.Top >= spanningArea.Min.Y &&
               screenBounds.Bottom <= spanningArea.Min.Y + spanningArea.Max.Y;
    }

    private static void AddScreenToSpanning(Screen screen, Screen[] screens)
    {
        var spanning = UserSettings.Config.OutputArea;
        var currentBounds = new ImRect(new Vector2(spanning.X, spanning.Y),
                                        new Vector2(spanning.Z, spanning.W));

        _spanningWorkList.Clear();
        var screenAlreadyIncluded = false;

        // Build list of screens in spanning area
        for (var i = 0; i < screens.Length; i++)
        {
            if (IsScreenInSpanningArea(screens[i], currentBounds))
            {
                _spanningWorkList.Add(screens[i]);
                if (screens[i] == screen)
                    screenAlreadyIncluded = true;
            }
        }

        if (!screenAlreadyIncluded)
            _spanningWorkList.Add(screen);

        // Now convert list to array only once for UpdateSpanningBounds
        UpdateSpanningBounds([.. _spanningWorkList]);
    }

    private static void RemoveScreenFromSpanning(Screen screen, Screen[] screens)
    {
        var spanning = UserSettings.Config.OutputArea;
        var currentBounds = new ImRect(new Vector2(spanning.X, spanning.Y),
                                        new Vector2(spanning.Z, spanning.W));

        _spanningWorkList.Clear();

        // Build list excluding the screen to remove
        for (var i = 0; i < screens.Length; i++)
        {
            if (IsScreenInSpanningArea(screens[i], currentBounds) && screens[i] != screen)
            {
                _spanningWorkList.Add(screens[i]);
            }
        }

        UpdateSpanningBounds(_spanningWorkList);
    }

    private static void UpdateSpanningBounds(IList<Screen> selectedScreens)
    {
        if (selectedScreens.Count == 0)
        {
            UserSettings.Config.OutputArea = new Vector4(0, 0, 0, 0);
            return;
        }

        var minX = selectedScreens[0].Bounds.X;
        var minY = selectedScreens[0].Bounds.Y;
        var maxX = selectedScreens[0].Bounds.Right;
        var maxY = selectedScreens[0].Bounds.Bottom;

        for (var i = 1; i < selectedScreens.Count; i++)
        {
            var bounds = selectedScreens[i].Bounds;
            if (bounds.X < minX) minX = bounds.X;
            if (bounds.Y < minY) minY = bounds.Y;
            if (bounds.Right > maxX) maxX = bounds.Right;
            if (bounds.Bottom > maxY) maxY = bounds.Bottom;
        }

        UserSettings.Config.OutputArea = new Vector4(
            minX, minY,
            maxX - minX, maxY - minY
        );
    }

    private static Rectangle GetOverallScreenBounds(Screen[] screens)
    {
        if (screens.Length == 0)
            return new Rectangle(0, 0, 0, 0);

        var minX = screens[0].Bounds.X;
        var minY = screens[0].Bounds.Y;
        var maxX = screens[0].Bounds.Right;
        var maxY = screens[0].Bounds.Bottom;

        for (var i = 1; i < screens.Length; i++)
        {
            var bounds = screens[i].Bounds;
            if (bounds.X < minX) minX = bounds.X;
            if (bounds.Y < minY) minY = bounds.Y;
            if (bounds.Right > maxX) maxX = bounds.Right;
            if (bounds.Bottom > maxY) maxY = bounds.Bottom;
        }

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

    private static bool IsSpanningAreaLargerThanScreens(Screen[] screens, ImRect spanningArea)
    {
        var undefinedSpanning = spanningArea.Max.X == 0 || spanningArea.Max.Y == 0;
        if (undefinedSpanning)
            return false;

        var totalScreenArea = 0;
        var screensInSpanningCount = 0;

        // Single pass without allocations
        for (var i = 0; i < screens.Length; i++)
        {
            if (IsScreenInSpanningArea(screens[i], spanningArea))
            {
                totalScreenArea += screens[i].Bounds.Width * screens[i].Bounds.Height;
                screensInSpanningCount++;
            }
        }

        if (screensInSpanningCount == 0)
            return false;

        var spanningAreaTotal = (int)(spanningArea.Max.X * spanningArea.Max.Y);
        return spanningAreaTotal > totalScreenArea;
    }

    private static void RefreshScreenCache()
    {
        // Only allocate if display settings actually changed
        // Screen.AllScreens allocates, so minimize calls
        var currentScreenCount = Screen.AllScreens.Length;

        if (_cachedScreens.Length != currentScreenCount)
        {
            _cachedScreens = Screen.AllScreens;
            _cachedOverallBounds = GetOverallScreenBounds(_cachedScreens);
            _layoutDirty = true;
            InvalidateStringCache(); // Clear cached strings
        }
        else
        {
            // Check for changes without LINQ
            var hasChanges = false;
            var newScreens = Screen.AllScreens;

            for (var i = 0; i < _cachedScreens.Length; i++)
            {
                if (_cachedScreens[i].DeviceName != newScreens[i].DeviceName ||
                    !_cachedScreens[i].Bounds.Equals(newScreens[i].Bounds))
                {
                    hasChanges = true;
                    break;
                }
            }

            if (hasChanges)
            {
                _cachedScreens = newScreens;
                _cachedOverallBounds = GetOverallScreenBounds(newScreens);
                _layoutDirty = true;
                InvalidateStringCache();
            }
        }

        // Spanning area refresh
        var spanning = UserSettings.Config.OutputArea;
        _cachedSpanning = new ImRect(new Vector2(spanning.X, spanning.Y),
                                      new Vector2(spanning.Z, spanning.W));

        _screensInSpanning.Clear();
        for (var i = 0; i < _cachedScreens.Length; i++)
        {
            if (IsScreenInSpanningArea(_cachedScreens[i], _cachedSpanning))
            {
                _screensInSpanning.Add(_cachedScreens[i]);
            }
        }
    }

    private static string GetCachedLabel(int index, bool isPrimary)
    {
        var key = isPrimary ? -(index + 1) : (index + 1);
        if (!_cachedLabels.TryGetValue(key, out var label))
        {
            label = $"{index + 1}{(isPrimary ? " (Primary)" : "")}";
            _cachedLabels[key] = label;
        }
        return label;
    }

    private static string GetCachedResolution(int width, int height)
    {
        var key = (width, height);
        if (!_cachedResolutions.TryGetValue(key, out var resolution))
        {
            resolution = $"{width}x{height}";
            _cachedResolutions[key] = resolution;
        }
        return resolution;
    }

    private static void InvalidateStringCache()
    {
        _cachedLabels.Clear();
        _cachedResolutions.Clear();
    }

    /** Helper: Compute scale to fit screens in UI  **/
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

    /** DrawScreenLayout() - optimized for low CPU **/
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

            drawList.AddRectFilled(min, max, UiColors.BackgroundButton);
            drawList.AddRect(min, max, UiColors.BackgroundFull.Fade(0.7f));

            // Label and resolution
            var label = GetCachedLabel(i, screen.Primary);
            var resolution = GetCachedResolution(b.Width, b.Height);
            var labelPos = new Vector2(x + w * 0.5f - ImGui.CalcTextSize(label).X / 2, y + h * 0.23f);
            drawList.AddText(labelPos, UiColors.Text, label);

            ImGui.PushFont(Fonts.FontSmall);
            var resPos = new Vector2(x + w * 0.5f - ImGui.CalcTextSize(resolution).X / 2, labelPos.Y + 19 * T3Ui.UiScaleFactor);
            drawList.AddText(resPos, UiColors.TextMuted, resolution);
            ImGui.PopFont();
        }

        var isSpanningValidForOverlay = _cachedSpanning.Max.X > 0 && _cachedSpanning.Max.Y > 0 && _screensInSpanning.Count > 1;
        if (isSpanningValidForOverlay)
        {
            var hasGaps = IsSpanningAreaLargerThanScreens(screens, _cachedSpanning);

            var rectMin = new Vector2(
                canvasPos.X + centerOffsetX + (_cachedSpanning.Min.X - overall.X) * scale,
                canvasPos.Y + (_cachedSpanning.Min.Y - overall.Y) * scale
            );
            var rectMax = rectMin + new Vector2(_cachedSpanning.Max.X * scale, _cachedSpanning.Max.Y * scale);

            drawList.AddRect(rectMin, rectMax, UiColors.BackgroundActive.Fade(0.5f), 0, ImDrawFlags.RoundCornersNone, 2);

            var labelHeight = 20 * T3Ui.UiScaleFactor;
            var labelRectMin = new Vector2(rectMin.X, rectMax.Y);
            var labelRectMax = new Vector2(rectMax.X, rectMax.Y + labelHeight);
            drawList.AddRectFilled(labelRectMin, labelRectMax, UiColors.BackgroundActive.Fade(0.3f));

            ImGui.PushFont(Fonts.FontSmall);
            var text = hasGaps ? "Spanning (with gaps)" : "Spanning";
            var textSize = ImGui.CalcTextSize(text);
            var textPos = new Vector2(
                labelRectMin.X + ((_cachedSpanning.Max.X * scale) - textSize.X) * 0.5f,
                labelRectMin.Y + (labelHeight - textSize.Y) * 0.5f
            );
            drawList.AddText(textPos, UiColors.Text, text);
            ImGui.PopFont();
        }

        /** Child for interactive buttons **/
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
    private static ImRect _cachedSpanning;
    private static readonly HashSet<Screen> _screensInSpanning = [];
    private static float _lastWindowWidth = -1;

    private static readonly Dictionary<int, string> _cachedLabels = [];
    private static readonly Dictionary<(int, int), string> _cachedResolutions = [];
    private static readonly List<Screen> _spanningWorkList = new(8);
}