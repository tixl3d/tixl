using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Utils;
using T3.Editor.Gui;
using T3.Editor.Gui.Styling;

namespace T3.Editor.Skills.Ui;

/// <summary>
/// A simple "celebration" effect that can be shown on events like "level completed".
/// </summary>
internal static class StarShowerEffect
{
    internal static void DrawAndUpdate()
    {
        var center = GetCenter();
        var dl = ImGui.GetForegroundDrawList();

        var progress = (float)(ImGui.GetTime() - _startTime) / 3.0f;

        for (var index = 0; index < _positions.Length; index++)
        {
            // Update position
            var rand = MathUtils.Hash01((uint)index);
            var p = _positions[index];
            var dFromCenter = p - center;
            var l = dFromCenter.Length();
            var dNorm = Vector2.Normalize(dFromCenter);
            //var f = 50f / (progress + 5f) + 0.2f * rand;
            var f = progress + 0.1f * rand;
            p += f * dNorm * (30 / (2 * f + 1)) + new Vector2(0, 3f) * (f + 0.5f);

            _positions[index] = p;

            Icons.DrawIconAtScreenPosition(Icon.Star, p, new Vector2(Icons.FontSize * 4f), dl, Color.Orange.Fade((1 - 1.5f * f).Clamp(0, 1)));
        }
    }

    internal static void Reset()
    {
        _startTime = ImGui.GetTime();
        float radius = 30;
        var center = GetCenter() + new Vector2(0, -10);
        for (var index = 0; index < _positions.Length; index++)
        {
            var f = (float)index / Count + 0.223f;
            _positions[index] = center + new Vector2(MathF.Sin(f * MathF.Tau),
                                                     MathF.Cos(f * MathF.Tau)) * radius;
        }
    }

    private static Vector2 GetCenter()
    {
        var vp = ImGui.GetMainViewport();
        var windowSize = vp.Size;
        var center = windowSize * 0.5f;
        return center + new Vector2(-260, -65) * T3Ui.UiScaleFactor;
    }

    private static readonly Vector2[] _positions = new Vector2[Count];

    private const int Count = 30;
    private static double _startTime;
}