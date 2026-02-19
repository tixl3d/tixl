using ImGuiNET;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.InputsAndTypes;

namespace T3.Editor.Gui.InputUi.CombinedInputs;

/// <summary>
/// Custom UI for editing ADSR envelope parameters stored in a Vector4 (X=Attack, Y=Decay, Z=Sustain, W=Release).
/// Used via MappedType attribute on Vector4 inputs.
/// </summary>
public sealed class AdsrEnvelopeInputUi : InputValueUi<Vector4>
{
    public override IInputUi Clone()
    {
        return new AdsrEnvelopeInputUi
        {
            InputDefinition = InputDefinition,
            Parent = Parent,
            PosOnCanvas = PosOnCanvas,
            Relevancy = Relevancy,
            Size = Size,
        };
    }

    protected override InputEditStateFlags DrawEditControl(string name, Symbol.Child.Input input, ref Vector4 envelope, bool readOnly)
    {
        var cloneIfModified = input.IsDefault;
        var modified = DrawAdsrControl(ref envelope, cloneIfModified);

        if (cloneIfModified && modified.HasFlag(InputEditStateFlags.Modified))
        {
            input.IsDefault = false;
        }
        return modified;
    }

    /// <summary>
    /// Draws a compact ADSR envelope editor with visual curve display.
    /// Vector4 layout: X=Attack, Y=Decay, Z=Sustain, W=Release
    /// </summary>
    public static InputEditStateFlags DrawAdsrControl(ref Vector4 envelope, bool cloneIfModified)
    {
        var modified = InputEditStateFlags.Nothing;
        var drawList = ImGui.GetWindowDrawList();
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var frameHeight = ImGui.GetFrameHeight();
        var envelopeHeight = frameHeight * 2f;
        var startPos = ImGui.GetCursorScreenPos();
        var envelopeArea = new ImRect(
            startPos,
            new Vector2(startPos.X + availableWidth, startPos.Y + envelopeHeight)
        );

        // Draw envelope background (match operator style)
        //drawList.AddRectFilled(envelopeArea.Min, envelopeArea.Max, UiColors.BackgroundFull.Fade(0.3f));

        // --- Draw area under envelope curve with dark color ---
        var attack = Math.Max(0.001f, envelope.X);
        var decay = Math.Max(0.001f, envelope.Y);
        var sustain = Math.Clamp(envelope.Z, 0f, 1f);
        var release = Math.Max(0.001f, envelope.W);
        const float sustainDuration = 0.2f;
        var width = envelopeArea.GetWidth();
        var height = envelopeArea.GetHeight();
        var totalTime = attack + decay + sustainDuration + release;
        var attackX = envelopeArea.Min.X + width * (attack / totalTime);
        var decayX = envelopeArea.Min.X + width * ((attack + decay) / totalTime);
        var sustainX = envelopeArea.Min.X + width * ((attack + decay + sustainDuration) / totalTime);
        var baseY = envelopeArea.Max.Y - 2;
        var peakY = envelopeArea.Min.Y + 2;
        var sustainY = envelopeArea.Max.Y - (height - 4) * sustain - 2;
        var points = new Vector2[5];
        points[0] = new Vector2(envelopeArea.Min.X, baseY);
        points[1] = new Vector2(attackX, peakY);
        points[2] = new Vector2(decayX, sustainY);
        points[3] = new Vector2(sustainX, sustainY);
        points[4] = new Vector2(envelopeArea.Max.X, baseY);
        var fillPoints = new Vector2[points.Length + 2];
        fillPoints[0] = new Vector2(envelopeArea.Min.X, baseY);
        for (var i = 0; i < points.Length; i++)
            fillPoints[i + 1] = points[i];
        fillPoints[^1] = new Vector2(envelopeArea.Max.X, baseY);
        drawList.AddConvexPolyFilled(ref fillPoints[0], fillPoints.Length, UiColors.BackgroundFull.Fade(0.3f));

        // Draw envelope curve and handles
        modified |= DrawEnvelopeCurveWithHandles(drawList, envelopeArea, ref envelope);

        // Make the envelope area interactive for dragging (legacy drag)
        ImGui.SetCursorScreenPos(envelopeArea.Min);
        ImGui.InvisibleButton("##envelope_drag", envelopeArea.GetSize());
        if (ImGui.IsItemActive() && _activeDragTarget == DragTarget.None)
        {
            modified |= HandleEnvelopeDrag(ref envelope, envelopeArea, cloneIfModified);
        }

        // Move cursor below the envelope graph
        ImGui.SetCursorScreenPos(new Vector2(startPos.X, envelopeArea.Max.Y + 2));
        modified |= DrawParameterRow(ref envelope, availableWidth, cloneIfModified);
        return modified;
    }

    // --- Draw envelope curve with handles ---
    private static InputEditStateFlags DrawEnvelopeCurveWithHandles(ImDrawListPtr drawList, ImRect area, ref Vector4 envelope)
    {
        var modified = InputEditStateFlags.Nothing;
        var attack = Math.Max(0.001f, envelope.X);
        var decay = Math.Max(0.001f, envelope.Y);
        var sustain = Math.Clamp(envelope.Z, 0f, 1f);
        var release = Math.Max(0.001f, envelope.W);
        const float sustainDuration = 0.2f;
        var width = area.GetWidth();
        var height = area.GetHeight();
        var totalTime = attack + decay + sustainDuration + release;
        var attackX = area.Min.X + width * (attack / totalTime);
        var decayX = area.Min.X + width * ((attack + decay) / totalTime);
        var sustainX = area.Min.X + width * ((attack + decay + sustainDuration) / totalTime);
        var sustainY = area.Max.Y - (height - 4) * sustain - 2;
        var peakY = area.Min.Y + 2;

        // --- Draw envelope curve with 5 points (classic style) ---
        var points = new Vector2[5];
        points[0] = new Vector2(area.Min.X, area.Max.Y - 2);      // Start
        points[1] = new Vector2(attackX, peakY);                  // Attack peak
        points[2] = new Vector2(decayX, sustainY);                // End of decay
        points[3] = new Vector2(sustainX, sustainY);              // End of sustain
        points[4] = new Vector2(area.Max.X, area.Max.Y - 2);      // End of release

        // Draw curve line
        drawList.AddPolyline(ref points[0], points.Length, UiColors.WidgetLine, ImDrawFlags.None, 1.5f);

        // Draw grid lines (matching AnimValueUi style)
        drawList.AddLine(new Vector2(attackX, area.Min.Y), new Vector2(attackX, area.Max.Y), UiColors.WidgetAxis, 1);
        drawList.AddLine(new Vector2(decayX, area.Min.Y), new Vector2(decayX, area.Max.Y), UiColors.WidgetAxis, 1);
        drawList.AddLine(new Vector2(sustainX, area.Min.Y), new Vector2(sustainX, area.Max.Y), UiColors.WidgetAxis, 1);

        // Draw sustain level line
        drawList.AddLine(new Vector2(decayX, sustainY), new Vector2(sustainX, sustainY), UiColors.WidgetAxis, 1);

        // Draw labels
        var labelColor = UiColors.TextMuted;
        ImGui.PushFont(Fonts.FontSmall);
        var labelY = area.Max.Y - 12;
        
        // Calculate label positions centered in each region
        var attackCenter = area.Min.X + (attackX - area.Min.X) / 2;
        var decayCenter = attackX + (decayX - attackX) / 2;
        var sustainCenter = decayX + (sustainX - decayX) / 2;
        var releaseCenter = sustainX + (area.Max.X - sustainX) / 2;
        
        // Get text width to center horizontally
        var aSize = ImGui.CalcTextSize("A");
        var dSize = ImGui.CalcTextSize("D");
        var sSize = ImGui.CalcTextSize("S");
        var rSize = ImGui.CalcTextSize("R");
        
        drawList.AddText(new Vector2(attackCenter - aSize.X / 2, labelY), labelColor, "A");
        drawList.AddText(new Vector2(decayCenter - dSize.X / 2, labelY), labelColor, "D");
        drawList.AddText(new Vector2(sustainCenter - sSize.X / 2, labelY), labelColor, "S");
        drawList.AddText(new Vector2(releaseCenter - rSize.X / 2, labelY), labelColor, "R");
        ImGui.PopFont();

        // --- Handle logic ---
        var scale = new Vector2(1, 1); // No zoom in compact UI
        var handles = new[]
        {
            new HandleInfo(DragTarget.Attack, new Vector2(attackX, peakY), "Attack"),
            new HandleInfo(DragTarget.Decay, new Vector2(decayX, sustainY), "Decay"),
            new HandleInfo(DragTarget.Sustain, new Vector2((decayX + sustainX) / 2, sustainY), "Sustain"),
            new HandleInfo(DragTarget.Release, new Vector2(area.Max.X - 2, area.Max.Y - 2), "Release")
        };
        modified |= DrawAndHandleHandles(drawList, area, handles, scale, ref envelope);
        return modified;
    }

    private enum DragTarget { None, Attack, Decay, Sustain, Release }
    private readonly struct HandleInfo(DragTarget target, Vector2 position, string label)
    {
        public readonly DragTarget Target = target;
        public readonly Vector2 Position = position;
        public readonly string Label = label;
    }

    // --- Draw and handle draggable handles ---
    private static InputEditStateFlags DrawAndHandleHandles(ImDrawListPtr drawList, ImRect area, HandleInfo[] handles, Vector2 scale, ref Vector4 envelope)
    {
        var modified = InputEditStateFlags.Nothing;
        var mousePos = ImGui.GetMousePos();
        // Diamond size and style matching AdsrEnvelopeUi
        var diamondSize = 8f;
        var half = diamondSize / 2f;
        var anyHandleActive = false;
        var hoveredTarget = DragTarget.None;

        for (int i = 0; i < handles.Length; i++)
        {
            var handle = handles[i];
            var id = $"##adsr_handle_{i}";
            ImGui.SetCursorScreenPos(handle.Position - new Vector2(half, half));
            ImGui.InvisibleButton(id, new Vector2(diamondSize, diamondSize));
            var isHovered = ImGui.IsItemHovered();
            var isActiveHandle = ImGui.IsItemActive();
            if (isHovered && !anyHandleActive)
            {
                hoveredTarget = handle.Target;
                // Set cursor type based on handle
                switch (handle.Target)
                {
                    case DragTarget.Attack:
                    case DragTarget.Decay:
                    case DragTarget.Release:
                        ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW);
                        break;
                    case DragTarget.Sustain:
                        ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNS);
                        break;
                }
                // Show tooltip for hovered handle
                ImGui.SetTooltip($"{handle.Label}: drag to adjust");
            }
            if (isActiveHandle)
            {
                hoveredTarget = handle.Target;
                anyHandleActive = true;
            }
            // Draw diamond (rotated square)
            var fillColor = isHovered || isActiveHandle ? UiColors.ForegroundFull : UiColors.StatusAnimated;
            var outlineColor = 0xFF000000; // Black border
            var center = handle.Position;
            var diamond = new Vector2[4];
            diamond[0] = new Vector2(center.X, center.Y - half); // top
            diamond[1] = new Vector2(center.X + half, center.Y); // right
            diamond[2] = new Vector2(center.X, center.Y + half); // bottom
            diamond[3] = new Vector2(center.X - half, center.Y); // left
            drawList.AddConvexPolyFilled(ref diamond[0], 4, fillColor);
            drawList.AddPolyline(ref diamond[0], 4, outlineColor, ImDrawFlags.Closed, 1f);

            // Handle dragging
            if (isActiveHandle)
            {
                var delta = ImGui.GetIO().MouseDelta;
                var width = area.GetWidth();
                var height = area.GetHeight();
                var timeSensitivity = 0.5f;
                if (ImGui.GetIO().KeyShift)
                    timeSensitivity *= 0.1f;
                var newEnvelope = envelope;
                switch (handle.Target)
                {
                    case DragTarget.Attack:
                        newEnvelope.X = Math.Max(0.001f, newEnvelope.X + delta.X / width * timeSensitivity);
                        break;
                    case DragTarget.Decay:
                        newEnvelope.Y = Math.Max(0.001f, newEnvelope.Y + delta.X / width * timeSensitivity);
                        break;
                    case DragTarget.Sustain:
                        newEnvelope.Z = Math.Clamp(newEnvelope.Z - delta.Y / height, 0f, 1f);
                        break;
                    case DragTarget.Release:
                        newEnvelope.W = Math.Max(0.001f, newEnvelope.W + delta.X / width * timeSensitivity);
                        break;
                }
                envelope = newEnvelope;
                modified = InputEditStateFlags.Modified;
            }
        }
        return modified;
    }

    private static DragTarget _activeDragTarget = DragTarget.None;

    private static InputEditStateFlags HandleEnvelopeDrag(ref Vector4 envelope, ImRect area, bool cloneIfModified)
    {
        var modified = InputEditStateFlags.Nothing;
        var mousePos = ImGui.GetMousePos();
        var mouseDelta = ImGui.GetIO().MouseDelta;

        if (Math.Abs(mouseDelta.X) < 0.001f && Math.Abs(mouseDelta.Y) < 0.001f)
            return modified;

        var attack = Math.Max(0.001f, envelope.X);
        var decay = Math.Max(0.001f, envelope.Y);
        var release = Math.Max(0.001f, envelope.W);
        const float sustainDuration = 0.2f;

        // Determine which segment we're in based on mouse position
        var width = area.GetWidth();
        var relX = (mousePos.X - area.Min.X) / width;

        var totalTime = attack + decay + sustainDuration + release;
        var attackEnd = attack / totalTime;
        var decayEnd = (attack + decay) / totalTime;
        var sustainEnd = (attack + decay + sustainDuration) / totalTime;

        var sensitivity = 0.01f;
        if (ImGui.GetIO().KeyShift)
            sensitivity *= 0.1f;

        if (relX < attackEnd)
        {
            envelope.X = Math.Max(0.001f, envelope.X + mouseDelta.X * sensitivity);
            modified = InputEditStateFlags.Modified;
        }
        else if (relX < decayEnd)
        {
            envelope.Y = Math.Max(0.001f, envelope.Y + mouseDelta.X * sensitivity);
            modified = InputEditStateFlags.Modified;
        }
        else if (relX < sustainEnd)
        {
            envelope.Z = Math.Clamp(envelope.Z - mouseDelta.Y * 0.01f, 0f, 1f);
            modified = InputEditStateFlags.Modified;
        }
        else
        {
            envelope.W = Math.Max(0.001f, envelope.W + mouseDelta.X * sensitivity);
            modified = InputEditStateFlags.Modified;
        }

        return modified;
    }

    private static InputEditStateFlags DrawParameterRow(ref Vector4 envelope, float availableWidth, bool cloneIfModified)
    {
        var modified = InputEditStateFlags.Nothing;
        var paramWidth = (availableWidth - 12) / 4;
        var frameHeight = ImGui.GetFrameHeight();
        var size = new Vector2(paramWidth, frameHeight);

        var attack = envelope.X;
        var decay = envelope.Y;
        var sustain = envelope.Z;
        var release = envelope.W;

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(3, 0));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(2, 2));

        // Attack
        ImGui.PushID("A");
        var editState = SingleValueEdit.Draw(ref attack, size, 0.001f, 10f, true, false, 0.01f, "A:{0:0.000}");
        if (editState.HasFlag(InputEditStateFlags.Modified))
        {
            modified |= InputEditStateFlags.Modified;
            envelope.X = Math.Max(0.001f, attack);
        }
        if (editState.HasFlag(InputEditStateFlags.Finished))
            modified |= InputEditStateFlags.Finished;
        ImGui.PopID();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Attack time (seconds)");

        ImGui.SameLine();

        // Decay
        ImGui.PushID("D");
        editState = SingleValueEdit.Draw(ref decay, size, 0.001f, 10f, true, false, 0.01f, "D:{0:0.000}");
        if (editState.HasFlag(InputEditStateFlags.Modified))
        {
            modified |= InputEditStateFlags.Modified;
            envelope.Y = Math.Max(0.001f, decay);
        }
        if (editState.HasFlag(InputEditStateFlags.Finished))
            modified |= InputEditStateFlags.Finished;
        ImGui.PopID();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Decay time (seconds)");

        ImGui.SameLine();

        // Sustain
        ImGui.PushID("S");
        editState = SingleValueEdit.Draw(ref sustain, size, 0f, 1f, true, true, 0.01f, "S:{0:0.00}");
        if (editState.HasFlag(InputEditStateFlags.Modified))
        {
            modified |= InputEditStateFlags.Modified;
            envelope.Z = Math.Clamp(sustain, 0f, 1f);
        }
        if (editState.HasFlag(InputEditStateFlags.Finished))
            modified |= InputEditStateFlags.Finished;
        ImGui.PopID();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Sustain level (0-1)");

        ImGui.SameLine();

        // Release
        ImGui.PushID("R");
        editState = SingleValueEdit.Draw(ref release, size, 0.001f, 10f, true, false, 0.01f, "R:{0:0.000}");
        if (editState.HasFlag(InputEditStateFlags.Modified))
        {
            modified |= InputEditStateFlags.Modified;
            envelope.W = Math.Max(0.001f, release);
        }
        if (editState.HasFlag(InputEditStateFlags.Finished))
            modified |= InputEditStateFlags.Finished;
        ImGui.PopID();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Release time (seconds)");

        ImGui.PopStyleVar(2);


        return modified;
    }

    protected override void DrawReadOnlyControl(string name, ref Vector4 value)
    {
        ImGui.TextUnformatted($"A:{value.X:F3} D:{value.Y:F3} S:{value.Z:F2} R:{value.W:F3}");
    }
}
