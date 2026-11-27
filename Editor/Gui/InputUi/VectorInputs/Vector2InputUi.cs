#nullable enable

using ImGuiNET;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Core.DataTypes;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.Interaction;
using T3.Core.Utils;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.UiModel.InputsAndTypes;
using T3.Serialization;

namespace T3.Editor.Gui.InputUi.VectorInputs;

internal sealed class Vector2InputUi : FloatVectorInputValueUi<Vector2>
{
    public Vector2InputUi() : base(2)
    {
    }

    public override IInputUi Clone()
    {
        return CloneWithType<Vector2InputUi>();
    }


    protected override InputEditStateFlags DrawEditControl(string name, Symbol.Child.Input input, ref Vector2 float2Value, bool readOnly)
    {
        float2Value.CopyTo(FloatComponents);

        var controlRatio = MathUtils.RemapAndClamp(ImGui.GetWindowWidth(), 280, 350, 1, 16f / 9);
        var showControl = (controlRatio > 1f && UseVec2Control != Vec2Controls.None);
        var rightPadding = showControl ? ImGui.GetFrameHeight() * controlRatio : 0;

        GetDrawRange(out var rangeMin, out var rangeMax, out var scale, out var clampMin, out var clampMax);
        var inputEditState = VectorValueEdit.Draw(FloatComponents, rangeMin, rangeMax, scale, clampMin, clampMax, rightPadding , Format);
        var shouldClamp = false;
            
        if (showControl)
        {
            ImGui.SameLine();
            ImGui.InvisibleButton("## dragging", new Vector2(rightPadding, ImGui.GetFrameHeight()));
            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            var drawList = ImGui.GetWindowDrawList();

            Color backgroundColor = ImGui.IsItemActive()
                                        ? UiColors.BackgroundActive.Fade(0.4f)
                                        : (UiColors.BackgroundButton.Fade(ImGui.IsItemHovered() ? 1 : 0.5f));
            drawList.AddRectFilled(min, max, backgroundColor);
            var normalizedVec = ((float2Value - new Vector2(rangeMin)) / (rangeMax - rangeMin) - new Vector2(0.5f)) * new Vector2(controlRatio,1) + new Vector2(0.5f);
            var valueColor = input == null ? UiColors.StatusAutomated
                        : input.IsDefault ? UiColors.TextMuted
                        : UiColors.ForegroundFull;
            var color = valueColor;

            var exceedsDragCanvas = normalizedVec.X < 0 || normalizedVec.X > 1 || normalizedVec.Y < 0 || normalizedVec.Y > 1;
            if (exceedsDragCanvas)
            {
                color = color.Fade(0.3f);
                normalizedVec = new Vector2(normalizedVec.X.Clamp(0, 1), normalizedVec.Y.Clamp(0, 1));
            }

            var controlPointScreenPos = new Vector2(
                                                    MathUtils.Lerp(min.X, max.X, normalizedVec.X),
                                                    MathUtils.Lerp(max.Y, min.Y, normalizedVec.Y)
                                                   );

            if (UseVec2Control == Vec2Controls.Position)
            {
                drawList.AddCircleFilled(controlPointScreenPos, 2, color);

                // Center lines...
                var center = (min + max) / 2;
                drawList.AddLine(new Vector2(center.X, min.Y), new Vector2(center.X, max.Y), UiColors.ForegroundFull.Fade(0.06f), 1);
                drawList.AddLine(new Vector2(min.X, center.Y), new Vector2(max.X, center.Y), UiColors.ForegroundFull.Fade(0.06f), 1);
            }
            else if (UseVec2Control == Vec2Controls.GainAndBias)
            {
                shouldClamp = true;
                drawList.AddCircleFilled(controlPointScreenPos, 2, color);

                const int steps = 15;
                var points = new Vector2[steps];
                    
                // Distribution...
                for(var i=0; i < steps ; i ++)
                {
                    var t = (float)i / (steps-1); 
                    var f = t.ApplyGainAndBias(float2Value.X, float2Value.Y);
                    var x = MathUtils.Lerp(min.X, max.X, f);
                    var y = MathUtils.Lerp(max.Y, min.Y, f);
                    // drawList.AddLine(new Vector2(x, min.Y), new Vector2(x, max.Y),
                    //                  UiColors.ForegroundFull.Fade(0.1f), 1);

                        
                    drawList.AddLine(new Vector2(min.X, y), new 
                                         Vector2(max.X, y),
                                     UiColors.ForegroundFull.Fade(0.1f), 1);
                        
                        
                    points[i] = new Vector2(MathUtils.Lerp(min.X, max.X, t), 
                                            MathUtils.Lerp(min.Y, max.Y, 1-f));
                }
                    
                drawList.AddPolyline(ref points[0], steps, UiColors.TextMuted, ImDrawFlags.None, 1);
            }
            else if (UseVec2Control == Vec2Controls.Range)
            {
                var defaultRange = input?.DefaultValue is InputValue<Vector2> typedDefault
                                       ? typedDefault.Value
                                       : float2Value;
                DrawRangeComparison(drawList, min, max, defaultRange, float2Value, valueColor);
            }

            if (ImGui.IsItemActivated())
            {
                _valueInDragStart = float2Value;
            }

            if (ImGui.IsItemActive())
            {
                inputEditState = InputEditStateFlags.Modified;
                var mouseDragDelta = ImGui.GetMouseDragDelta();

                var speedFactor = 1f;

                if (ImGui.GetIO().KeyShift)
                {
                    speedFactor *= 0.1f;
                }

                if (ImGui.GetIO().KeyAlt)
                {
                    speedFactor *= 10f;
                }

                var dx = mouseDragDelta.X * 0.005f * speedFactor;
                var dy = mouseDragDelta.Y * 0.005f * speedFactor;
                
                if (UseVec2Control != Vec2Controls.Range)
                {
                    FloatComponents[0] = _valueInDragStart.X + dx;
                    FloatComponents[1] = _valueInDragStart.Y - dy;
                }
                else
                {
                    var zoom = MathF.Pow(4,-dy);
                    var center = (_valueInDragStart.X + _valueInDragStart.Y) / 2 + dx*zoom * 0.4f;
                    var width =   (_valueInDragStart.Y - _valueInDragStart.X) * zoom;
                    
                    FloatComponents[0] = center - width/2;
                    FloatComponents[1] = center + width/2;
                }

                if (shouldClamp)
                {
                    FloatComponents[0] = FloatComponents[0].Clamp(rangeMin, rangeMax);
                    FloatComponents[1] = FloatComponents[1].Clamp(rangeMin, rangeMax);
                }
            }

            if (ImGui.IsItemActivated())
            {
                inputEditState |= InputEditStateFlags.Started;
            }

            if (ImGui.IsItemDeactivated())
            {
                inputEditState = InputEditStateFlags.Finished;
            }
        }

        if (readOnly)
            return InputEditStateFlags.Nothing;

        float2Value = new Vector2(FloatComponents[0], FloatComponents[1]);

        return inputEditState;
    }

    /// <summary>
    /// Draws the default range as a faded line on top and the current range below it. The default stays centered,
    /// and the horizontal scale fits both, so an offset or zoomed range reads as a shifted or stretched lower line.
    /// </summary>
    private static void DrawRangeComparison(ImDrawListPtr drawList, Vector2 min, Vector2 max, Vector2 defaultRange, Vector2 range, Color rangeColor)
    {
        var defaultCenter = (defaultRange.X + defaultRange.Y) / 2;
        var defaultHalfExtent = MathF.Abs(defaultRange.X - defaultCenter);
        var rangeHalfExtent = MathF.Max(MathF.Abs(range.X - defaultCenter), MathF.Abs(range.Y - defaultCenter));

        var width = max.X - min.X;
        var height = max.Y - min.Y;
        var centerX = (min.X + max.X) / 2;

        // The scale follows the default and only shrinks once the current range would leave its wider margin.
        const float minExtent = 0.000001f;
        var pixelsPerUnit = defaultHalfExtent > minExtent
                                ? width * (0.5f - DefaultRangePadding) / defaultHalfExtent
                                : float.PositiveInfinity;
        if (rangeHalfExtent > minExtent)
            pixelsPerUnit = MathF.Min(pixelsPerUnit, width * (0.5f - RangePadding) / rangeHalfExtent);

        if (float.IsPositiveInfinity(pixelsPerUnit))
            pixelsPerUnit = 0;

        var thickness = MathF.Max(1, MathF.Round(T3Ui.UiScaleFactor));
        var triangleSize = 4 * T3Ui.UiScaleFactor;
        var defaultLineY = MathF.Round(min.Y + height * 0.375f);
        var rangeLineY = MathF.Round(min.Y + height * 0.625f);

        DrawRangeLine(drawList,
                      centerX + (defaultRange.X - defaultCenter) * pixelsPerUnit,
                      centerX + (defaultRange.Y - defaultCenter) * pixelsPerUnit,
                      defaultLineY, thickness, -triangleSize, UiColors.ForegroundFull.Fade(0.25f));
        DrawRangeLine(drawList,
                      centerX + (range.X - defaultCenter) * pixelsPerUnit,
                      centerX + (range.Y - defaultCenter) * pixelsPerUnit,
                      rangeLineY, thickness, triangleSize, rangeColor);
    }

    /** Triangles grow inward from start and end, so an inverted range shows them pointing outward. A negative triangleHeight points up. */
    private static void DrawRangeLine(ImDrawListPtr drawList, float startX, float endX, float y, float thickness, float triangleHeight, Color color)
    {
        startX = MathF.Round(startX);
        endX = MathF.Round(endX);
        drawList.AddRectFilled(new Vector2(MathF.Min(startX, endX), y),
                               new Vector2(MathF.Max(startX, endX) + thickness, y + thickness),
                               color);

        var baseY = triangleHeight < 0 ? y : y + thickness;
        var tipY = baseY + triangleHeight;
        var size = MathF.Abs(triangleHeight);
        var startCorner = new Vector2(startX, baseY);
        var endCorner = new Vector2(endX + thickness, baseY);

        // Keep a clockwise winding for ImGui's antialiased fill when the triangles flip downward.
        if (triangleHeight < 0)
        {
            drawList.AddTriangleFilled(startCorner, new Vector2(startX, tipY), new Vector2(startX + size, baseY), color);
            drawList.AddTriangleFilled(endCorner, new Vector2(endCorner.X - size, baseY), new Vector2(endCorner.X, tipY), color);
        }
        else
        {
            drawList.AddTriangleFilled(startCorner, new Vector2(startX + size, baseY), new Vector2(startX, tipY), color);
            drawList.AddTriangleFilled(endCorner, new Vector2(endCorner.X, tipY), new Vector2(endCorner.X - size, baseY), color);
        }
    }

    public override void ApplyValueToAnimation(IInputSlot inputSlot, InputValue inputValue, Animator animator, double time)
    {
        if (inputValue is not InputValue<Vector2> typedInputValue)
            return;

        if (!animator.TryGetCurvesForInputSlot(inputSlot, out var curves))
        {
            Log.Warning("Can't find Vec2 animation curves?");
            return;
        }
        
        typedInputValue.Value.CopyTo(FloatComponents);
        Curve.UpdateCurveValues(curves, time, FloatComponents);
    }

    public override bool DrawSettings()
    {
        var modified = base.DrawSettings();

        FormInputs.DrawFieldSetHeader("Show 2D Control");
        var tmpForRef = UseVec2Control;
        if (FormInputs.AddEnumDropdown(ref tmpForRef, null))
        {
            modified = true;
            UseVec2Control = tmpForRef;
        }

        return modified;
    }

    public override void Write(JsonTextWriter writer)
    {
        base.Write(writer);

        if (UseVec2Control != Vec2Controls.None)
            writer.WriteObject(nameof(UseVec2Control), UseVec2Control.ToString());
    }

    public override void Read(JToken? inputToken)
    {
        base.Read(inputToken);

        // Back-compat: older files use the legacy spelling "BiasAndGain"
        var controlName = inputToken?[nameof(UseVec2Control)]?.Value<string>();
        UseVec2Control = controlName switch
                             {
                                 "BiasAndGain"                                             => Vec2Controls.GainAndBias,
                                 _ when Enum.TryParse(controlName, out Vec2Controls value) => value,
                                 _                                                         => Vec2Controls.None,
                             };
    }

    public Vec2Controls UseVec2Control;
    private static Vector2 _valueInDragStart;

    /** Fraction of the control width left free on each side of the default range when nothing forces a rescale. */
    private const float DefaultRangePadding = 0.3f;

    /** Fraction of the control width the current range keeps free on each side before the view zooms out. */
    private const float RangePadding = 0.1f;

    public enum Vec2Controls
    {
        None,
        Position,
        GainAndBias,
        Range,
    }
}