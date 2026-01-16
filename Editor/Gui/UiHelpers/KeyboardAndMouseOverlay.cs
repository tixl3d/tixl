using ImGuiNET;
using T3.Core.Animation;
using T3.Core.Utils;
using T3.Editor.Gui.Styling;
using T3.SystemUi;

namespace T3.Editor.Gui.UiHelpers;

public static class KeyboardAndMouseOverlay
{
    public static void Draw()
    {
        if (!UserSettings.Config.ShowInteractionOverlay)
        {
            return;
        }

        _widgetFade = 1-(float)((Playback.RunTimeInSecs - _lastInteractionTime) / FadeoutDuration).Clamp(0, 1);

        var dl = ImGui.GetForegroundDrawList();

        var pos = ImGui.GetMousePos() + new Vector2(30, -100) * T3Ui.UiScaleFactor;
        if(pos.X + 120 > ImGui.GetIO().DisplaySize.X)
            pos.X = ImGui.GetIO().DisplaySize.X - 120;

        if (pos.Y + 90 > ImGui.GetIO().DisplaySize.Y)
        {
            var dy = 90 + pos.Y - ImGui.GetIO().DisplaySize.Y;
            pos.Y -= dy;
            pos.X += dy/2;
        }

        var panelSize = new Vector2(120, 90) * T3Ui.UiScaleFactor;
        dl.AddRectFilled(pos, pos + panelSize, UiColors.BackgroundFull.Fade(0.7f * _widgetFade), 10);

        UpdatePressedKeys();
        DrawKeys(pos, dl);

        var radius = 20f * T3Ui.UiScaleFactor;
        var thickness = 26f * T3Ui.UiScaleFactor;
        var center = pos + new Vector2(panelSize.X / 2, 65 * T3Ui.UiScaleFactor);
        var spacing = 2f * T3Ui.UiScaleFactor;
        var halfspace = spacing * 0.5f;
        const float radspace = 22f;
        const int segments = 12;

        if (ImGui.IsMouseDown(ImGuiMouseButton.Left)
            || ImGui.IsMouseDown(ImGuiMouseButton.Middle)
            || ImGui.IsMouseDown(ImGuiMouseButton.Right))
        {
            _lastInteractionTime = Playback.RunTimeInSecs;   
        }

        // Left mouse 
        {
            var color = ImGui.IsMouseDown(ImGuiMouseButton.Left) ? UiColors.ForegroundFull.Fade(_widgetFade) : UiColors.ForegroundFull.Fade(0.1f * _widgetFade);
            dl.PathClear();
            dl.PathLineTo(center + new Vector2(-halfspace, -radius));
            dl.PathArcTo(center - new Vector2(spacing, 0), radius, -MathF.PI / 2, -MathF.PI, segments);
            dl.PathLineTo(center + new Vector2(-radspace, 15) * T3Ui.UiScaleFactor);
            dl.PathStroke(color, ImDrawFlags.None, thickness);
            
            // Check for double click
            if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                _lastDoubleClickTime = Playback.RunTimeInSecs;
            }
            else if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && _lastDoubleClickTime >= 0)
            {
                _lastDoubleClickTime = 0;
            }
        }

        DrawDoubleClickIndicator(center + new Vector2(-radspace - 12, -11) * T3Ui.UiScaleFactor, dl);

        // right mouse 
        {
            var color = ImGui.IsMouseDown(ImGuiMouseButton.Right) ? UiColors.ForegroundFull.Fade(_widgetFade) : UiColors.ForegroundFull.Fade(0.1f * _widgetFade);
            dl.PathClear();
            dl.PathLineTo(center + new Vector2(halfspace, -radius));
            dl.PathArcTo(center + new Vector2(spacing, 0), radius, -MathF.PI / 2, 0, segments);
            dl.PathLineTo(center + new Vector2(radspace, 15) * T3Ui.UiScaleFactor);
            dl.PathStroke(color, ImDrawFlags.None, thickness);
        }

        // middle mouse
        {
            var color = ImGui.IsMouseDown(ImGuiMouseButton.Middle) ? UiColors.ForegroundFull.Fade(_widgetFade) : UiColors.ForegroundFull.Fade(0.1f * _widgetFade);
            var size = new Vector2(14, 24) * T3Ui.UiScaleFactor;
            var min = center - size * 0.5f + new Vector2(0, 7) * T3Ui.UiScaleFactor;
            dl.AddRectFilled(min, min + size, color, 4 * T3Ui.UiScaleFactor);
        }

        // mouse wheel
        {
            var fadeFromTime = 1-(float)((Playback.RunTimeInSecs - _lastMouseWheelInteractionTime) / FadeoutDuration).Clamp(0, 1);
            var mouseDelta = ImGui.GetIO().MouseWheel;
            if (mouseDelta != 0)
            {
                _lastMouseWheelInteractionTime = Playback.RunTimeInSecs;
                _lastInteractionTime = Playback.RunTimeInSecs;
                _wheelSpin -= mouseDelta;
            }

            _dampedWheelSpin = DampFunctions.DampenFloat(_wheelSpin, _dampedWheelSpin, 0.001f, ref _dampedWheelSpinVelocity, DampFunctions.Methods.DampedSpring);
            if (double.IsInfinity(_dampedWheelSpin) || double.IsNaN(_dampedWheelSpin))
            {
                _dampedWheelSpin = 0;
            }

            const int lineCount = 5;
            var height = 34 * T3Ui.UiScaleFactor;
            var size = new Vector2(12, 1) * T3Ui.UiScaleFactor;
            var min = center - size * 0.5f + new Vector2(0, -10) * T3Ui.UiScaleFactor;
            float step = height / (lineCount + 1);

            for (int i = 0; i < lineCount; i++)
            {
                var f = (float)i / lineCount;
                var fadeEdge =1- MathF.Abs((f - 0.5f) * 2).Clamp(0, 1);
                var offset = new Vector2(0, step * i + step * MathUtils.Fmod(_dampedWheelSpin + 0.5f,  1));

                var color = UiColors.ForegroundFull.Fade(fadeFromTime * fadeEdge);
                dl.AddRectFilled(min + offset, min + size + offset, color, 10);
            }
        }
    }

    private static void DrawKeys(Vector2 pos, ImDrawListPtr dl)
    {
        var keyPos = pos + new Vector2(4, 4);
        foreach (var k in _previousKeys)
        {
            var label = k.Label;
            var labelSize = ImGui.CalcTextSize(label);
            var rectMin = keyPos;
            var size = labelSize + new Vector2(8, 2);
            var fade = MathF.Pow((1 - k.FadeProgress).Clamp(0f, 1f), 4);
            dl.AddRectFilled(rectMin,
                             rectMin + size,
                             UiColors.ForegroundFull.Fade(fade * _widgetFade),
                             3);
            dl.AddText(rectMin + new Vector2(4, 0), UiColors.BackgroundFull.Fade(_widgetFade), label);
            keyPos.X += (size.X + 3);
        }
    }

    private static void UpdatePressedKeys()
    {
        var time = Playback.RunTimeInSecs;

        for (var index = 0; index < KeyHandler.PressedKeys.Count; index++)
        {
            var pressed = KeyHandler.PressedKeys[index];
            var status = _keyStates[index];
            if (status == null)
                continue;

            if (pressed != status.IsPressed)
            {
                if (pressed)
                {
                    _previousKeys.Remove(status);
                    _previousKeys.Add(status);

                }

                status.IsPressed = pressed;
            }

            if (pressed)
            {
                status.ReleaseTime = time;
                _lastInteractionTime = time;
            }
            else
            {
                if(status.FadeProgress > 1 )
                    _previousKeys.Remove(status);
            }
        }
    }

    private static void DrawDoubleClickIndicator(Vector2 position, ImDrawListPtr dl)
    {
        var timeSince = (float)(Playback.RunTimeInSecs - _lastDoubleClickTime);

        if (timeSince > FadeoutDuration)
            return;

        var textColor = UiColors.ForegroundFull.Fade(_widgetFade);
        dl.AddText(Fonts.FontLarge, Fonts.FontLarge.FontSize, position, textColor, "×2");
    }

    private static readonly List<KeyStatus> _previousKeys = new();
    private static double _lastInteractionTime = double.NegativeInfinity;
    private static float _widgetFade = 0;
    private static double _lastMouseWheelInteractionTime;

    private static double _lastDoubleClickTime = double.NegativeInfinity;

    private sealed class KeyStatus
    {
        public KeyStatus(int keyIndex, string label, Icon icon = Icon.None )
        {
            if (keyIndex >= KeyLookupCount)
                return;

            Icon = icon;
            Label = label;


            _keyStates[keyIndex] = this;
        }

        public float FadeProgress
        {
            get
            {
                var timeSinceRelease = (float)(Playback.RunTimeInSecs - ReleaseTime);
                return timeSinceRelease / FadeoutDuration;
            }
        }

        public bool IsPressed;
        public readonly string Label;
        public Icon Icon;
        public double ReleaseTime = double.NegativeInfinity;

    }

    private const float FadeoutDuration = 2;
    private const int KeyLookupCount = 512;
    private static float _wheelSpin;
    private static float _dampedWheelSpin;
    private static float _dampedWheelSpinVelocity;

    private static readonly KeyStatus[] _keyStates = new KeyStatus[KeyLookupCount];

    // ReSharper disable once UnusedMember.Local
    private static readonly KeyStatus[] _keyDefinitions = {
                                                                      new(8, "Backspace"),
                                                                      new(9, "Tab"),
                                                                      new(13, "Return"),
                                                                      new(16, "Shift"),
                                                                      new(17, "Ctrl"),
                                                                      new(18, "Alt"),
                                                                      new(20, "Caps"),
                                                                      new(27, "Esc"),
                                                                      new(32, "Space"),
                                                                      new(33, "PageUp"),
                                                                      new(34, "PageDown"),
                                                                      new(35, "End"),
                                                                      new(36, "Home"),
                                                                      new(37, "Left", Icon.ChevronLeft),
                                                                      new(38, "Up", Icon.ChevronUp),
                                                                      new(39, "Right", Icon.ChevronRight),
                                                                      new(40, "Down", Icon.ChevronDown),
                                                                      new(45, "Ins"),
                                                                      new(46, "Del"),
                                                                      new(48, "0"),
                                                                      new(49, "1"),
                                                                      new(50, "2"),
                                                                      new(51, "3"),
                                                                      new(52, "4"),
                                                                      new(53, "5"),
                                                                      new(54, "6"),
                                                                      new(55, "7"),
                                                                      new(56, "8"),
                                                                      new(57, "9"),
                                                                      new(65, "A"),
                                                                      new(66, "B"),
                                                                      new(67, "C"),
                                                                      new(68, "D"),
                                                                      new(69, "E"),
                                                                      new(70, "F"),
                                                                      new(71, "G"),
                                                                      new(72, "H"),
                                                                      new(73, "I"),
                                                                      new(74, "J"),
                                                                      new(75, "K"),
                                                                      new(76, "L"),
                                                                      new(77, "M"),
                                                                      new(78, "N"),
                                                                      new(79, "O"),
                                                                      new(80, "P"),
                                                                      new(81, "Q"),
                                                                      new(82, "R"),
                                                                      new(83, "S"),
                                                                      new(84, "T"),
                                                                      new(85, "U"),
                                                                      new(86, "V"),
                                                                      new(87, "W"),
                                                                      new(88, "X"),
                                                                      new(89, "Y"),
                                                                      new(90, "Z"),
                                                                      new(112, "F1"),
                                                                      new(113, "F2"),
                                                                      new(114, "F3"),
                                                                      new(115, "F4"),
                                                                      new(116, "F5"),
                                                                      new(117, "F6"),
                                                                      new(118, "F7"),
                                                                      new(119, "F8"),
                                                                      new(120, "F9"),
                                                                      new(121, "F10"),
                                                                      new(122, "F11"),
                                                                      new(123, "F12"),
                                                                      new(187, "+"),
                                                                      new(187, "="),
                                                                      new(189, "-"),
                                                                      new(219, "["),
                                                                      new(221, "]"),
                                                                      new(188, ","),
                                                                      new(190, "."),
                                                                      new(191, "/"),
                                                                      new(192, "|"),
                                                                      new(186, ";"),
                                                                      new(226, "`"),
                                                                      new(220, "#"),
                                                                  };

}