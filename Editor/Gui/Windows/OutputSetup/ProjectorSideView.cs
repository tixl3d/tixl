#nullable enable
using ImGuiNET;
using T3.Core.Output;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Styling;
using T3.Editor.UiModel.InputsAndTypes;
using Color = T3.Core.DataTypes.Vector.Color;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// The projector seen from the side: its zoom and lens shift drawn as where the picture lands on a wall at the throw
/// distance, relative to the lens axis. The distance, the picture's height and its lower edge's height above the lens
/// are values to drag or type, so measuring a real projection with a tape gives the zoom and the lens shift.
/// </summary>
internal static class ProjectorSideView
{
    /** Which end of the zoom range a measured picture height sets. */
    private enum RangeEnds
    {
        Wide,
        Tele,
    }

    public static void Draw(Setup setup, OutputDefinition output)
    {
        var camera = output.Camera ?? _defaults;
        var scale = T3Ui.UiScaleFactor;
        var width = ImGui.GetContentRegionAvail().X;
        var height = MathF.Round(DrawHeight * scale);
        var origin = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new Vector2(width, height));
        var end = ImGui.GetCursorScreenPos();
        if (width < 200 * scale)
            return;

        var distance = MathF.Max(camera.ThrowDistance, MinThrowDistance);
        var shift = camera.LensAxis.Y;
        GetEdges(camera.ManualFovYDegrees, shift, distance, out var top, out var bottom);
        GetEdges(camera.ZoomRangeDegrees.X, shift, distance, out var wideTop, out var wideBottom);
        GetEdges(camera.ZoomRangeDegrees.Y, shift, distance, out var teleTop, out var teleBottom);
        var yMin = MathF.Min(0, MathF.Min(bottom, MathF.Min(wideBottom, teleBottom)));
        var yMax = MathF.Max(0, MathF.Max(top, MathF.Max(wideTop, teleTop)));

        // One scale for both directions, so the angles are true.
        var fieldSize = new Vector2(MathF.Round(FieldWidth * scale), ImGui.GetFrameHeight());
        var left = 28 * scale;
        var right = 38 * scale + fieldSize.X;
        var above = fieldSize.Y * 0.5f + 2 * scale;
        var below = fieldSize.Y + 8 * scale;
        var metresToPx = MathF.Min((width - left - right) / distance, (height - above - below) / MathF.Max(yMax - yMin, 0.0001f));
        var lensX = MathF.Round(origin.X + left);
        var screenX = MathF.Round(lensX + distance * metresToPx);
        var floorY = origin.Y + height - below;
        float ToY(float metres) => MathF.Round(floorY - (metres - yMin) * metresToPx);

        var axisY = ToY(0);
        var dl = ImGui.GetWindowDrawList();
        var muted = UiColors.TextMuted;
        var dimension = UiColors.StatusActivated;
        var lensPoint = new Vector2(lensX, axisY);

        // The zoom's two ends, faint; the picture where the zoom sits now, with its cone.
        var isFixedLens = MathF.Abs(camera.ZoomRangeDegrees.X - camera.ZoomRangeDegrees.Y) < 0.01f;
        if (!isFixedLens)
        {
            DrawRangeEnd(dl, lensPoint, screenX, ToY(wideTop), ToY(wideBottom));
            DrawRangeEnd(dl, lensPoint, screenX, ToY(teleTop), ToY(teleBottom));

            dl.AddLine(new Vector2(screenX, ToY(wideTop)), new Vector2(screenX, ToY(wideBottom)), UiColors.ForegroundFull.Fade(0.3f),
                       MathF.Max(1, MathF.Round(2 * scale)));
        }

        var topY = ToY(top);
        var bottomY = ToY(bottom);
        dl.AddTriangleFilled(lensPoint, new Vector2(screenX, topY), new Vector2(screenX, bottomY), UiColors.ForegroundFull.Fade(0.08f));
        dl.AddLine(lensPoint, new Vector2(screenX, topY), UiColors.ForegroundFull.Fade(0.25f));
        dl.AddLine(lensPoint, new Vector2(screenX, bottomY), UiColors.ForegroundFull.Fade(0.25f));
        dl.AddLine(lensPoint, new Vector2(screenX + 20 * scale, axisY), muted.Fade(0.6f));

        var bodySize = new Vector2(MathF.Round(14 * scale), MathF.Round(9 * scale));
        dl.AddRectFilled(new Vector2(lensX - bodySize.X, axisY - MathF.Round(bodySize.Y * 0.5f)),
                         new Vector2(lensX, axisY + MathF.Round(bodySize.Y * 0.5f)), muted, 2 * scale);
        dl.AddLine(new Vector2(screenX, topY), new Vector2(screenX, bottomY), UiColors.ForegroundFull, MathF.Max(1, MathF.Round(2 * scale)));

        // Dimensions in columns: the current picture's lower edge above the lens, then the tele and wide heights.
        float Column(int index) => MathF.Round(screenX + (8 + index * 8) * scale);
        DrawDimension(dl, new Vector2(Column(0), axisY), new Vector2(Column(0), bottomY), dimension, scale);
        DrawDimension(dl, new Vector2(Column(1), ToY(teleBottom)), new Vector2(Column(1), ToY(teleTop)), dimension, scale);
        DrawDimension(dl, new Vector2(Column(2), ToY(wideBottom)), new Vector2(Column(2), ToY(wideTop)), dimension, scale);

        var distanceY = MathF.Round(floorY + 4 * scale + fieldSize.Y * 0.5f);
        DrawDimension(dl, new Vector2(lensX, distanceY), new Vector2(screenX, distanceY), dimension, scale);

        // The editable values beside their dimensions, in order and pushed apart where they would overlap.
        var fieldX = Column(3);
        _fieldYs[0] = (ToY(wideTop) + ToY(wideBottom)) * 0.5f;
        _fieldYs[1] = (ToY(teleTop) + ToY(teleBottom)) * 0.5f;
        _fieldYs[2] = (bottomY + axisY) * 0.5f;
        SpreadFields(_fieldYs, fieldSize.Y + 2 * scale);

        var wideHeight = wideTop - wideBottom;
        var teleHeight = teleTop - teleBottom;
        var distanceState = DrawField("distance", new Vector2(MathF.Round((lensX + screenX - fieldSize.X) * 0.5f), distanceY - fieldSize.Y * 0.5f),
                                      fieldSize, ref distance, "Distance from the lens to the wall, in metres, measured square to the wall. Changes neither zoom nor lens shift.");
        var wideState = DrawField("wide", new Vector2(fieldX, MathF.Round(_fieldYs[0] - fieldSize.Y * 0.5f)), fieldSize, ref wideHeight,
                                  "Height of the picture with the zoom at its wide end, on a wall at this distance, in metres. Sets that end of the zoom range; the same as tele for a fixed lens.");
        var teleState = DrawField("tele", new Vector2(fieldX, MathF.Round(_fieldYs[1] - fieldSize.Y * 0.5f)), fieldSize, ref teleHeight,
                                  "Height of the picture with the zoom at its tele end, on a wall at this distance, in metres. Sets that end of the zoom range; the same as wide for a fixed lens.");
        var gapState = DrawField("lower", new Vector2(fieldX, MathF.Round(_fieldYs[2] - fieldSize.Y * 0.5f)), fieldSize, ref bottom,
                                 "How far the picture's lower edge is above the lens, in metres (negative below it), with the projector standing level and the zoom where it is now. Sets the lens shift.");

        var aspect = ProjectorPresets.AspectOf(output);
        if (BeginEdit(setup, distanceState))
            CameraOf(output).ThrowDistance = Math.Clamp(distance, MinThrowDistance, 1000);

        EndEdit(setup, distanceState, "Change throw distance");

        if (BeginEdit(setup, wideState))
            SetRangeEnd(CameraOf(output), RangeEnds.Wide, FieldOfViewForHeight(wideHeight, distance), aspect);

        SortWhenFinished(output, wideState);
        EndEdit(setup, wideState, "Measure wide picture height");

        if (BeginEdit(setup, teleState))
            SetRangeEnd(CameraOf(output), RangeEnds.Tele, FieldOfViewForHeight(teleHeight, distance), aspect);

        SortWhenFinished(output, teleState);
        EndEdit(setup, teleState, "Measure tele picture height");

        if (BeginEdit(setup, gapState))
            SetLowerEdge(CameraOf(output), distance, bottom);

        EndEdit(setup, gapState, "Measure lower edge");

        ImGui.SetCursorScreenPos(end);
        ImGui.Dummy(Vector2.Zero);
    }

    /** Pushes field centres apart where they'd overlap, keeping their order from top to bottom. */
    private static void SpreadFields(float[] ys, float minGap)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            for (var i = 0; i < ys.Length; i++)
            {
                for (var j = i + 1; j < ys.Length; j++)
                {
                    var gap = ys[j] - ys[i];
                    if (MathF.Abs(gap) >= minGap)
                        continue;

                    var middle = (ys[i] + ys[j]) * 0.5f;
                    var direction = gap >= 0 ? 1 : -1;
                    ys[i] = middle - direction * minGap * 0.5f;
                    ys[j] = middle + direction * minGap * 0.5f;
                }
            }
        }
    }

    private static float FieldOfViewForHeight(float pictureHeight, float distance)
    {
        return Math.Clamp(2 * MathF.Atan(MathF.Max(pictureHeight, MinPictureHeight) / (2 * distance)) * 180 / MathF.PI, 1, 170);
    }

    /// <summary>Sets one end of the zoom range, keeping the zoom at its place within the range.</summary>
    private static void SetRangeEnd(OutputDefinition.ProjectorCamera camera, RangeEnds ends, float fieldOfView, float aspect)
    {
        var position = ProjectorPresets.ZoomPosition(camera, aspect);
        var range = camera.ZoomRangeDegrees;
        camera.ZoomRangeDegrees = ends switch
                                      {
                                          RangeEnds.Wide => new Vector2(fieldOfView, range.Y),
                                          _ => new Vector2(range.X, fieldOfView),
                                      };
        ProjectorPresets.SetZoomPosition(camera, position, aspect);
    }

    /** A zoom end's picture: its edges' rays from the lens, faint. */
    private static void DrawRangeEnd(ImDrawListPtr dl, Vector2 lensPoint, float screenX, float topY, float bottomY)
    {
        dl.AddLine(lensPoint, new Vector2(screenX, topY), UiColors.ForegroundFull.Fade(0.12f));
        dl.AddLine(lensPoint, new Vector2(screenX, bottomY), UiColors.ForegroundFull.Fade(0.12f));
    }

    /** The vertical lens shift that puts the current picture's lower edge <paramref name="bottom"/> metres above the lens. */
    private static void SetLowerEdge(OutputDefinition.ProjectorCamera camera, float distance, float bottom)
    {
        var halfHeightPerMetre = MathF.Tan(camera.ManualFovYDegrees * MathF.PI / 360);
        camera.LensAxis = new Vector2(camera.LensAxis.X, -1 - bottom / (distance * halfHeightPerMetre));
    }

    /** The picture's upper and lower edge above the lens axis, in metres, at <paramref name="distance"/>. */
    private static void GetEdges(float fieldOfViewDegrees, float lensAxisY, float distance, out float top, out float bottom)
    {
        // The picture spans NDC −1..1 and the lens axis lands at NDC lensAxisY; half the picture is tan(fov / 2).
        var halfHeightPerMetre = MathF.Tan(fieldOfViewDegrees * MathF.PI / 360);
        top = distance * (1 - lensAxisY) * halfHeightPerMetre;
        bottom = distance * (-1 - lensAxisY) * halfHeightPerMetre;
    }

    private static InputEditStateFlags DrawField(string id, Vector2 position, Vector2 size, ref float value, string tooltip)
    {
        ImGui.SetCursorScreenPos(position);
        ImGui.PushID(id);
        var state = SingleValueEdit.Draw(ref value, size, scale: 0.005f, format: "{0:0.00} m");
        ImGui.PopID();
        CustomComponents.TooltipForLastItem(tooltip);
        return state;
    }

    /** Opens the undo step of a field's drag or typed value; true when its new value is to be applied now. */
    private static bool BeginEdit(Setup setup, InputEditStateFlags state)
    {
        if (state == InputEditStateFlags.Nothing)
            return false;

        SetupParameterView.BeginFieldUndo(setup, state);
        return (state & InputEditStateFlags.Modified) != 0;
    }

    /** Puts the zoom range wide end first once a height edit is done; never while typing, which would swap the fields. */
    private static void SortWhenFinished(OutputDefinition output, InputEditStateFlags state)
    {
        if ((state & InputEditStateFlags.Finished) != 0 && output.Camera is { } camera)
            ProjectorPresets.SortZoomRange(camera);
    }

    /** Closes the undo step once the drag or typed value is finished. */
    private static void EndEdit(Setup setup, InputEditStateFlags state, string undoName)
    {
        if (state != InputEditStateFlags.Nothing)
            SetupParameterView.CommitFieldUndo(setup, undoName, state);
    }

    /** The output's camera, created on the first edit. */
    private static OutputDefinition.ProjectorCamera CameraOf(OutputDefinition output) => output.Camera ??= new OutputDefinition.ProjectorCamera();

    /** A measuring line with arrowheads at both ends. */
    private static void DrawDimension(ImDrawListPtr dl, Vector2 from, Vector2 to, Color color, float scale)
    {
        var length = Vector2.Distance(from, to);
        if (length < 1)
            return;

        var direction = (to - from) / length;
        var side = new Vector2(-direction.Y, direction.X) * (3 * scale);
        var head = direction * MathF.Min(5 * scale, length * 0.5f);
        dl.AddLine(from, to, color);
        dl.AddTriangleFilled(from, from + head + side, from + head - side, color);
        dl.AddTriangleFilled(to, to - head + side, to - head - side, color);
    }

    /** What a projector with no camera data yet is drawn as: centred lens, the default zoom and distance. */
    private static readonly OutputDefinition.ProjectorCamera _defaults = new();

    /** Field centres, wide height, tele height and lower edge; reused every frame. */
    private static readonly float[] _fieldYs = new float[3];

    private const float DrawHeight = 150;
    private const float FieldWidth = 64;
    private const float MinThrowDistance = 0.05f;
    private const float MinPictureHeight = 0.001f;
}
