using ImGuiNET;
using T3.Core.Animation;
using T3.Core.DataTypes.Vector;
using T3.Core.Utils;

namespace T3.Editor.Gui.UiHelpers;

/// <summary>
/// The mouse drawing of the interaction overlay: three buttons and a spinning scroll wheel. Shared by
/// the local overlay and by the remote cursors of a collaboration session, so both read the same.
/// Every instance keeps its own wheel animation.
/// </summary>
internal sealed class MouseInteractionGraphic
{
    /// <summary>Feeds scroll input in wheel ticks; positive scrolls forward, away from the user.</summary>
    public void AddWheelTicks(float ticks)
    {
        if (ticks == 0)
            return;

        _lastWheelTime = Playback.RunTimeInSecs;
        _wheelSpin -= ticks;
    }

    /// <summary>True while the wheel is moving or has not faded out yet.</summary>
    public bool IsWheelVisible => Playback.RunTimeInSecs - _lastWheelTime < WheelFadeoutDuration;

    /// <param name="center">Middle of the mouse body in screen space.</param>
    /// <param name="fade">Overall opacity; the wheel additionally fades out on its own after scrolling stops.</param>
    /// <param name="scale">Size factor; 1 is the size the local overlay uses at 100% UI scale.</param>
    public void Draw(ImDrawListPtr dl, Vector2 center, bool leftDown, bool rightDown, bool middleDown,
                     Color color, float fade, float scale)
    {
        var radius = 20f * scale;
        var thickness = 26f * scale;
        var spacing = 2f * scale;
        var halfSpace = spacing * 0.5f;
        const float radSpace = 22f;
        const int segments = 12;

        // Left button
        {
            var buttonColor = leftDown ? color.Fade(fade) : color.Fade(0.1f * fade);
            dl.PathClear();
            dl.PathLineTo(center + new Vector2(-halfSpace, -radius));
            dl.PathArcTo(center - new Vector2(spacing, 0), radius, -MathF.PI / 2, -MathF.PI, segments);
            dl.PathLineTo(center + new Vector2(-radSpace, 15) * scale);
            dl.PathStroke(buttonColor, ImDrawFlags.None, thickness);
        }

        // Right button
        {
            var buttonColor = rightDown ? color.Fade(fade) : color.Fade(0.1f * fade);
            dl.PathClear();
            dl.PathLineTo(center + new Vector2(halfSpace, -radius));
            dl.PathArcTo(center + new Vector2(spacing, 0), radius, -MathF.PI / 2, 0, segments);
            dl.PathLineTo(center + new Vector2(radSpace, 15) * scale);
            dl.PathStroke(buttonColor, ImDrawFlags.None, thickness);
        }

        // Middle button
        {
            var buttonColor = middleDown ? color.Fade(fade) : color.Fade(0.1f * fade);
            var size = new Vector2(14, 24) * scale;
            var min = center - size * 0.5f + new Vector2(0, 7) * scale;
            dl.AddRectFilled(min, min + size, buttonColor, 4 * scale);
        }

        DrawWheel(dl, center, color, fade, scale);
    }

    private void DrawWheel(ImDrawListPtr dl, Vector2 center, Color color, float fade, float scale)
    {
        var wheelFade = 1 - (float)((Playback.RunTimeInSecs - _lastWheelTime) / WheelFadeoutDuration).Clamp(0, 1);

        _dampedWheelSpin = DampFunctions.DampenFloat(_wheelSpin, _dampedWheelSpin, 0.001f, ref _dampedWheelSpinVelocity,
                                                     DampFunctions.Methods.DampedSpring);
        if (double.IsInfinity(_dampedWheelSpin) || double.IsNaN(_dampedWheelSpin))
        {
            _dampedWheelSpin = 0;
        }

        const int lineCount = 5;
        var height = 34 * scale;
        var size = new Vector2(12, 1) * scale;
        var min = center - size * 0.5f + new Vector2(0, -10) * scale;
        var step = height / (lineCount + 1);

        for (var i = 0; i < lineCount; i++)
        {
            var f = (float)i / lineCount;
            var fadeEdge = 1 - MathF.Abs((f - 0.5f) * 2).Clamp(0, 1);
            var offset = new Vector2(0, step * i + step * MathUtils.Fmod(_dampedWheelSpin + 0.5f, 1));

            dl.AddRectFilled(min + offset, min + size + offset, color.Fade(wheelFade * fadeEdge * fade), 10);
        }
    }

    private const float WheelFadeoutDuration = 2;
    private double _lastWheelTime = double.NegativeInfinity;
    private float _wheelSpin;
    private float _dampedWheelSpin;
    private float _dampedWheelSpinVelocity;
}
