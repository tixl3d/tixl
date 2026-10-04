#nullable enable
using ImGuiNET;
using T3.Core.Output;
using T3.Editor.Gui.Interaction.CanvasEditing;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.Selection;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// The wall-drawing tool on a floor plan's Board card: from an open end of the run a line follows the cursor,
/// snapped to 45° steps of the last wall or to the room's own horizontal and vertical (Shift draws free), or
/// set to a typed length, and every click plants
/// the next corner with a wall on the new edge. Clicking the run's other end closes the room; Escape or a
/// right-click ends the tool. Entered from the plus at an open end, the plan's menu, or right after a plan is
/// started from a surface.
/// </summary>
internal sealed partial class SetupOutputView
{
    /// <summary>Asks the next Board frame to start drawing at the end of this plan — set by actions that create one.</summary>
    public static Guid PendingPlanDrawId;

    /// <summary>Whether the tool owns the Board's mouse right now.</summary>
    private bool IsDrawingPlan => _planDrawId != Guid.Empty;

    /// <summary>
    /// The plus at each open end of a plan's run: clicking it starts drawing there. Shown whether or not the
    /// plan is selected — its corners and edges are live either way, and a control that only appears once
    /// something is selected reads as if the plan itself were inert. Selection lifts it like the corners,
    /// rather than summoning it. Drawn with the card, before its grab, so the press is an item press.
    /// </summary>
    private void DrawPlanDrawEntries(FloorPlan plan, Vector2 origin, bool isSelected)
    {
        if (plan.IsClosed || plan.Vertices.Count < 2 || IsDrawingPlan)
            return;

        for (var atEnd = 0; atEnd < 2; atEnd++)
        {
            var last = atEnd == 1 ? plan.Vertices[^1] : plan.Vertices[0];
            var previous = atEnd == 1 ? plan.Vertices[^2] : plan.Vertices[1];
            var along = last - previous;
            if (along.LengthSquared() < 0.0001f)
                continue;

            along /= along.Length();
            var scale = T3Ui.UiScaleFactor;
            var screen = _boardProjection.CanvasToScreen(origin + last);
            var screenAlong = _boardProjection.CanvasToScreen(origin + last + along) - screen;
            screenAlong /= MathF.Max(screenAlong.Length(), 0.001f);
            var centre = screen + screenAlong * 18 * scale;
            var radius = 8 * scale;

            ImGui.PushID(atEnd);
            ImGui.SetCursorScreenPos(centre - new Vector2(radius));
            ImGui.InvisibleButton("##drawFrom", new Vector2(radius * 2));
            var hovered = ImGui.IsItemHovered();
            var dl = ImGui.GetWindowDrawList();
            var hue = SetupColors.ForKind(SetupEntityKinds.FloorPlan);
            var resting = isSelected ? hue : hue.Fade(0.6f); // the corners' own selected/unselected pair
            dl.AddCircleFilled(centre, radius, (hovered ? hue : resting).Fade(_boardLayerFade));
            Icons.DrawIconAtScreenPosition(Icon.Plus, centre - new Vector2(8 * scale), dl,
                                           UiColors.ForegroundFull.Fade(_boardLayerFade * (hovered || isSelected ? 1f : 0.8f)));
            if (hovered)
                CustomComponents.TooltipForLastItem("Draw walls from here", "Click plants a corner and raises a wall; Escape ends.");

            if (ImGui.IsItemClicked())
                BeginPlanDraw(plan.Id, atEnd == 1);

            ImGui.PopID();
        }
    }

    private void BeginPlanDraw(Guid planId, bool atEnd)
    {
        _planDrawId = planId;
        _planDrawAtEnd = atEnd;
        _planDrawTyped.Clear();
    }

    private void EndPlanDraw()
    {
        _planDrawId = Guid.Empty;
        _planDrawTyped.Clear();
    }

    /// <summary>The tool's frame: the preview from the open end to the cursor, and the keys and clicks that drive it.</summary>
    private void HandlePlanDraw(Setup setup, SetupEntitySelection? selection)
    {
        if (PendingPlanDrawId != Guid.Empty)
        {
            if (setup.FindFloorPlan(PendingPlanDrawId) is { IsClosed: false, Vertices.Count: >= 2 })
                BeginPlanDraw(PendingPlanDrawId, atEnd: true);

            PendingPlanDrawId = Guid.Empty;
        }

        if (!IsDrawingPlan)
            return;

        var plan = setup.FindFloorPlan(_planDrawId);
        if (plan == null || plan.IsClosed || plan.Vertices.Count < 2)
        {
            EndPlanDraw();
            return;
        }

        var focused = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && !ImGui.GetIO().WantTextInput;
        if (focused && (ImGui.IsKeyPressed(ImGuiKey.Escape, false) || ImGui.IsMouseClicked(ImGuiMouseButton.Right)))
        {
            EndPlanDraw();
            return;
        }

        if (focused)
            ReadTypedLength();

        var origin = plan.BoardPlacement?.Position ?? Vector2.Zero;
        var anchor = _planDrawAtEnd ? plan.Vertices[^1] : plan.Vertices[0];
        var previous = _planDrawAtEnd ? plan.Vertices[^2] : plan.Vertices[1];
        var otherEnd = _planDrawAtEnd ? plan.Vertices[0] : plan.Vertices[^1];
        var cursor = _boardProjection.ScreenToCanvas(ImGui.GetMousePos()) - origin;

        // Where the corner would land: onto the other end to close the room, else along a snapped direction,
        // at the typed length when there is one.
        var closing = plan.Vertices.Count >= 3 && (cursor - otherEnd).Length() < BoardSnapThreshold() && !ImGui.GetIO().KeyShift;
        var point = closing ? otherEnd : SnapPlanDirection(anchor, previous, cursor);
        var typedLength = ParseTypedLength();
        var guided = false;
        if (!closing && typedLength > 0)
        {
            var direction = point - anchor;
            if (direction.LengthSquared() < 0.000001f)
                direction = anchor - previous;

            point = anchor + direction / MathF.Max(direction.Length(), 0.0001f) * typedLength;
        }
        else if (!closing && !ImGui.GetIO().KeyShift)
        {
            // The first wall's direction, seen from the run's other end — what a closing wall would meet.
            var firstDirection = _planDrawAtEnd ? plan.Vertices[1] - plan.Vertices[0] : plan.Vertices[^1] - plan.Vertices[^2];
            guided = SnapAlongRayToStart(anchor, otherEnd, firstDirection, ref point);
        }

        DrawPlanDrawPreview(origin, anchor, point, closing, guided ? otherEnd : null);

        var commit = ImGui.IsWindowHovered() && !ImGui.IsAnyItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left)
                     || focused && ImGui.IsKeyPressed(ImGuiKey.Enter, false);
        if (!commit)
            return;

        if ((point - anchor).Length() < SurfaceGeometry.MinSize)
            return;

        var atEnd = _planDrawAtEnd;
        SetupUndo.RunUndoable(closing ? "Close room" : "Draw wall", setup, () =>
                                                                          {
                                                                              int newSegment;
                                                                              if (closing)
                                                                              {
                                                                                  plan.IsClosed = true;
                                                                                  plan.EnsureWallSlots();
                                                                                  newSegment = plan.SegmentCount - 1;
                                                                              }
                                                                              else if (atEnd)
                                                                              {
                                                                                  plan.Vertices.Add(point);
                                                                                  plan.EnsureWallSlots();
                                                                                  newSegment = plan.SegmentCount - 1;
                                                                              }
                                                                              else
                                                                              {
                                                                                  // Prepending shifts every segment along by one; the slots go with them.
                                                                                  plan.Vertices.Insert(0, point);
                                                                                  plan.WallSurfaceIds.Insert(0, Guid.Empty);
                                                                                  plan.EnsureWallSlots();
                                                                                  newSegment = 0;
                                                                              }

                                                                              FloorPlanSync.SetWall(setup, plan, newSegment, true);
                                                                              FloorPlanSync.Apply(setup, plan);
                                                                          });
        _planDrawTyped.Clear();
        if (closing)
            EndPlanDraw();
    }

    /// <summary>
    /// The next corner along the cursor, snapped to 45° steps of the last wall <b>or</b> to the plan's
    /// horizontal and vertical, whichever the cursor is nearer to — a room drawn off a slanted wall usually
    /// squares up with the venue again, not with that wall. Shift draws free.
    /// </summary>
    private static Vector2 SnapPlanDirection(Vector2 anchor, Vector2 previous, Vector2 cursor)
    {
        var toCursor = cursor - anchor;
        var length = toCursor.Length();
        if (length < 0.0001f || ImGui.GetIO().KeyShift)
            return cursor;

        var cursorAngle = MathF.Atan2(toCursor.Y, toCursor.X);
        var reference = anchor - previous;
        var referenceAngle = reference.LengthSquared() < 0.0001f ? 0f : MathF.Atan2(reference.Y, reference.X);

        const float alongWallStep = MathF.PI / 4;
        var alongWall = referenceAngle + MathF.Round((cursorAngle - referenceAngle) / alongWallStep) * alongWallStep;

        const float axisStep = MathF.PI / 2;
        var alongAxis = MathF.Round(cursorAngle / axisStep) * axisStep;

        var snapped = AngleDistance(cursorAngle, alongAxis) < AngleDistance(cursorAngle, alongWall) ? alongAxis : alongWall;
        return anchor + new Vector2(MathF.Cos(snapped), MathF.Sin(snapped)) * length;
    }

    /// <summary>The shorter way round between two angles, so candidates can be compared however they wrapped.</summary>
    private static float AngleDistance(float a, float b)
    {
        var delta = MathF.Abs(a - b) % (MathF.PI * 2);
        return delta > MathF.PI ? MathF.PI * 2 - delta : delta;
    }

    /// <summary>
    /// Slides the corner along its ray onto the places that square up with the run's start, when the cursor
    /// is near one: level with the start (the next wall can run straight into it), or where a closing wall
    /// would meet the first wall at a right angle. True when it snapped, so a guide can show why.
    /// </summary>
    private bool SnapAlongRayToStart(Vector2 anchor, Vector2 start, Vector2 firstDirection, ref Vector2 point)
    {
        var ray = point - anchor;
        var length = ray.Length();
        if (length < 0.0001f)
            return false;

        ray /= length;
        var threshold = BoardSnapThreshold();

        // Level with the start: the foot of its perpendicular onto the ray.
        var level = Vector2.Dot(start - anchor, ray);
        if (level > SurfaceGeometry.MinSize && MathF.Abs(level - length) < threshold)
        {
            point = anchor + ray * level;
            return true;
        }

        // Square to the first wall: where the ray crosses the line through the start perpendicular to it.
        var firstLength = firstDirection.Length();
        if (firstLength < 0.0001f)
            return false;

        firstDirection /= firstLength;
        var along = Vector2.Dot(ray, firstDirection);
        if (MathF.Abs(along) < 0.01f)
            return false;

        var square = Vector2.Dot(start - anchor, firstDirection) / along;
        if (square > SurfaceGeometry.MinSize && MathF.Abs(square - length) < threshold)
        {
            point = anchor + ray * square;
            return true;
        }

        return false;
    }

    private void DrawPlanDrawPreview(Vector2 origin, Vector2 anchor, Vector2 point, bool closing, Vector2? guideFrom)
    {
        var dl = ImGui.GetWindowDrawList();
        var scale = T3Ui.UiScaleFactor;
        var hue = SetupColors.ForKind(SetupEntityKinds.FloorPlan);
        var a = _boardProjection.CanvasToScreen(origin + anchor);
        var b = _boardProjection.CanvasToScreen(origin + point);
        if (guideFrom is { } from)
            dl.AddLine(_boardProjection.CanvasToScreen(origin + from), b, UiColors.Selection.Fade(0.5f), 1 * scale);

        dl.AddLine(a, b, hue.Fade(0.9f), 3 * scale);
        dl.AddCircleFilled(b, 5 * scale, closing ? UiColors.Selection : hue);

        var along = b - a;
        if (along.LengthSquared() < 1f)
            return;

        along /= along.Length();
        var label = _planDrawTyped.Length > 0 ? $"{_planDrawTyped} m" : closing ? "close" : $"{(point - anchor).Length():0.##} m";
        CanvasDraw.TextAlong(dl, Fonts.FontSmall, Fonts.FontSmall.FontSize, (a + b) * 0.5f + new Vector2(along.Y, -along.X) * 10 * scale, along,
                             UiColors.Text, label);
    }

    /// <summary>Digits, a decimal separator and Backspace build the length; anything else is left alone.</summary>
    private void ReadTypedLength()
    {
        for (var digit = 0; digit < 10; digit++)
        {
            if (ImGui.IsKeyPressed(ImGuiKey._0 + digit, false) || ImGui.IsKeyPressed(ImGuiKey.Keypad0 + digit, false))
                _planDrawTyped.Append((char)('0' + digit));
        }

        if ((ImGui.IsKeyPressed(ImGuiKey.Period, false) || ImGui.IsKeyPressed(ImGuiKey.Comma, false) || ImGui.IsKeyPressed(ImGuiKey.KeypadDecimal, false))
            && !_planDrawTyped.ToString().Contains('.'))
        {
            _planDrawTyped.Append('.');
        }

        if (ImGui.IsKeyPressed(ImGuiKey.Backspace, false) && _planDrawTyped.Length > 0)
            _planDrawTyped.Length--;
    }

    private float ParseTypedLength()
    {
        return _planDrawTyped.Length > 0
               && float.TryParse(_planDrawTyped.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var length)
                   ? length
                   : 0f;
    }

    private Guid _planDrawId;
    private bool _planDrawAtEnd;
    private readonly System.Text.StringBuilder _planDrawTyped = new();
}
