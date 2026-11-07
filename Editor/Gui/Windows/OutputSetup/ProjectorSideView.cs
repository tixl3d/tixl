#nullable enable
using ImGuiNET;
using T3.Core.Output;
using T3.Editor.Gui.Styling;
using Color = T3.Core.DataTypes.Vector.Color;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// The projector seen from the side, drawn from its zoom and lens shift: where the picture lands relative to the lens
/// axis and how large it gets. At the distance to the surface it was solved from, or per metre of throw before that,
/// so the two numbers that are hard to picture read as a shape.
/// </summary>
internal static class ProjectorSideView
{
    public static void Draw(Setup setup, OutputDefinition.ProjectorCamera camera)
    {
        var scale = T3Ui.UiScaleFactor;
        var width = ImGui.GetContentRegionAvail().X;
        var height = MathF.Round(DrawHeight * scale);
        var origin = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new Vector2(width, height));
        if (width < 60 * scale)
            return;

        var isSolved = TryGetThrowDistance(setup, camera, out var distance);
        if (!isSolved)
            distance = 1;

        // The picture spans NDC −1..1; the lens axis lands at NDC LensAxis.Y, half the picture being tan(fov / 2).
        var halfHeightPerMetre = MathF.Tan(camera.ManualFovYDegrees * MathF.PI / 360);
        if (isSolved && camera.Lens is { } lens)
            halfHeightPerMetre = MathF.Tan(lens.FieldOfViewY * 0.5f);

        var axis = camera.LensAxis.Y;
        var top = distance * (1 - axis) * halfHeightPerMetre;
        var bottom = distance * (-1 - axis) * halfHeightPerMetre;
        var yMin = MathF.Min(0, bottom);
        var yMax = MathF.Max(0, top);

        // One scale for both directions, so the angles are true.
        var left = 28 * scale;
        var right = 56 * scale;
        var above = 8 * scale;
        var below = 26 * scale;
        var metresToPx = MathF.Min((width - left - right) / distance, (height - above - below) / MathF.Max(yMax - yMin, 0.0001f));
        var lensX = MathF.Round(origin.X + left);
        var screenX = MathF.Round(lensX + distance * metresToPx);
        var floorY = origin.Y + height - below;
        float ToY(float metres) => MathF.Round(floorY - (metres - yMin) * metresToPx);

        var axisY = ToY(0);
        var topY = ToY(top);
        var bottomY = ToY(bottom);
        var dl = ImGui.GetWindowDrawList();
        var muted = UiColors.TextMuted;
        var dimension = UiColors.StatusActivated;

        // Cone of light, its centre ray and the lens axis.
        var lensPoint = new Vector2(lensX, axisY);
        dl.AddTriangleFilled(lensPoint, new Vector2(screenX, topY), new Vector2(screenX, bottomY), UiColors.ForegroundFull.Fade(0.08f));
        dl.AddLine(lensPoint, new Vector2(screenX, topY), UiColors.ForegroundFull.Fade(0.25f));
        dl.AddLine(lensPoint, new Vector2(screenX, bottomY), UiColors.ForegroundFull.Fade(0.25f));
        dl.AddLine(lensPoint, new Vector2(screenX, (topY + bottomY) * 0.5f), UiColors.ForegroundFull.Fade(0.15f));
        dl.AddLine(lensPoint, new Vector2(screenX + 8 * scale, axisY), muted.Fade(0.6f));

        // The projector, its lens at the apex; the picture.
        var bodySize = new Vector2(MathF.Round(14 * scale), MathF.Round(9 * scale));
        dl.AddRectFilled(new Vector2(lensX - bodySize.X, axisY - MathF.Round(bodySize.Y * 0.5f)),
                         new Vector2(lensX, axisY + MathF.Round(bodySize.Y * 0.5f)), muted, 2 * scale);
        dl.AddLine(new Vector2(screenX, topY), new Vector2(screenX, bottomY), UiColors.ForegroundFull, MathF.Max(1, MathF.Round(2 * scale)));

        // Throw distance under it all.
        var distanceY = MathF.Round(floorY + 14 * scale);
        DrawDimension(dl, new Vector2(lensX, distanceY), new Vector2(screenX, distanceY), dimension, scale);
        DrawChip(dl, new Vector2((lensX + screenX) * 0.5f, distanceY), isSolved ? $"{distance:0.00} m" : "1 m", scale);

        // Picture height, right of the picture.
        var heightX = MathF.Round(screenX + 22 * scale);
        DrawDimension(dl, new Vector2(heightX, topY), new Vector2(heightX, bottomY), dimension, scale);
        DrawChip(dl, new Vector2(heightX + 16 * scale, (topY + bottomY) * 0.5f), $"{top - bottom:0.00}", scale);

        // How far the picture's bottom edge sits off the lens axis.
        if (MathF.Abs(bottomY - axisY) >= 4 * scale)
        {
            var gapX = MathF.Round(screenX + 8 * scale);
            DrawDimension(dl, new Vector2(gapX, bottomY), new Vector2(gapX, axisY), dimension, scale);
            DrawChip(dl, new Vector2(gapX - 22 * scale, (bottomY + axisY) * 0.5f), $"{MathF.Abs(bottom):0.00}", scale);
        }
    }

    /// <summary>
    /// Distance along the projector's view to the surface it was solved from: what the picture height and the offset
    /// are measured at. False before a solve, or when that surface is gone.
    /// </summary>
    private static bool TryGetThrowDistance(Setup setup, OutputDefinition.ProjectorCamera camera, out float distance)
    {
        distance = 0;
        if (camera.Pose is not { } pose || setup.FindSurface(camera.SolvedFromSurfaceId) is not { } surface)
            return false;

        SurfaceGeometry.LocalBounds(surface, out var min, out var max);
        var centre = Vector3.Transform(new Vector3((min + max) * 0.5f, 0), (surface.Placement?.Pose ?? Pose.Identity).ToWorldMatrix());
        var forward = Vector3.Transform(-Vector3.UnitZ, pose.Orientation);
        distance = Vector3.Dot(centre - pose.Position, forward);
        return float.IsFinite(distance) && distance > 0.01f;
    }

    /** A measuring line with arrowheads at both ends. */
    private static void DrawDimension(ImDrawListPtr dl, Vector2 from, Vector2 to, Color color, float scale)
    {
        var length = Vector2.Distance(from, to);
        if (length < 1)
            return;

        var direction = (to - from) / length;
        var side = new Vector2(-direction.Y, direction.X) * (3 * scale);
        var head = direction * (5 * scale);
        dl.AddLine(from, to, color);
        dl.AddTriangleFilled(from, from + head + side, from + head - side, color);
        dl.AddTriangleFilled(to, to - head + side, to - head - side, color);
    }

    /** A value in a small rounded chip, centred on a point. */
    private static void DrawChip(ImDrawListPtr dl, Vector2 centre, string text, float scale)
    {
        ImGui.PushFont(Fonts.FontSmall);
        var size = ImGui.CalcTextSize(text);
        var padding = new Vector2(5, 1) * scale;
        var min = new Vector2(MathF.Round(centre.X - size.X * 0.5f - padding.X), MathF.Round(centre.Y - size.Y * 0.5f - padding.Y));
        var max = min + size + padding * 2;
        dl.AddRectFilled(min, max, UiColors.BackgroundButton, 3 * scale);
        dl.AddText(min + padding, UiColors.TextMuted, text);
        ImGui.PopFont();
    }

    private const float DrawHeight = 110;
}
