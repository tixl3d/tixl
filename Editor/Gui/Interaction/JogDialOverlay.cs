using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Utils;
using T3.Editor.Gui.Styling;

namespace T3.Editor.Gui.Interaction;

/// <summary>
/// Draws a circular dial to manipulate values with various speeds
/// </summary>
internal static class JogDialOverlay
{
    internal static bool Draw(ref double value, bool restarted, Vector2 center, double min = double.NegativeInfinity,
                              double max = double.PositiveInfinity,
                              float scale = 0.1f, 
                              bool clampMin = false, 
                              bool clampMax= false)
    {
        var modified = false;
        _drawList = ImGui.GetForegroundDrawList();
        _io = ImGui.GetIO();
        _center = center;
            
        if (restarted)
        {
            _baseLog10Speed = (int)(Math.Log10(scale)+3.5f);
            _originalValue = value;
            _unclampedValue = value;
            _min = min;
            _max = max;
            _clampMin = clampMin;
            _clampMax = clampMax;
                
            _state = States.WaitingInNeutral;
        }

        for (int ringIndex = 0; ringIndex < RingCount; ringIndex++)
        {
            modified |= DialRing.Draw(ringIndex);
        }

        if (modified)
        {
            value = ClampedValue;
        }

        return modified;
    }

    private const int RingCount = 2;
    private static double _unclampedValue;

    private static double ClampedValue => MathUtils.OptionalClamp(_unclampedValue,_min, _clampMin, _max, _clampMax);

    private static float _baseLog10Speed = 1;
    private static double _originalValue;

    private static float AdjustedBaseLog10Scale
    {
        get
        {
            if (_io.KeyAlt)
            {
                return _baseLog10Speed+1;
            }

            if (_io.KeyShift)
            {
                return _baseLog10Speed-1;
            }

            return _baseLog10Speed;
        }
    }

    private static double _min;
    private static double _max;
    private static bool _clampMin;
    private static bool _clampMax;

    private const float NeutralRadius = 15;

    private static ImDrawListPtr _drawList;
    private static ImGuiIOPtr _io;
    private static Vector2 _center;
    private static float DistanceToCenter => Vector2.Distance(_io.MousePos, _center);

    private static readonly Color _segmentColor = new(0.2f, 0.2f, 0.2f, 0.35f);
    private static readonly Color _activeSegmentColor = new(1f, 1f, 1f, 0.5f);

    private enum States
    {
        Hidden,
        WaitingInNeutral,
        Manipulating,
    }

    private static States _state = States.Hidden;

    private static class DialRing
    {
        internal static bool Draw(int ringIndex)
        {
            _ringIndex = ringIndex;
            _drawList.AddCircle(_center,
                                radius: Radius + RingWidth / 2,
                                _segmentColor,
                                num_segments: 64,
                                thickness: RingWidth - Padding);

            DrawTicks();
            DrawValueLabels();

            var valueTickColor = new Color(1, 1, 1, IsActive ? 1 : 0.4f);
            DrawTick(ClampedValue / Pow, valueTickColor, lineThickness: IsActive ? 2 : 1);
            DrawValueDifference();
            return HandleInteraction();
        }

        private static void DrawTicks()
        {
            for (int lineIndex = 0; lineIndex < 100; lineIndex++)
            {
                var f = lineIndex * 0.01f;
                var alpha = (lineIndex % 10) == 0
                                ? 0.3f
                                : 0.1f;
                alpha *= IsActive ? 1 : 0.4f;
                var color = new Color(0, 0, 0, alpha);
                var lineThickness = lineIndex == 0 ? 4 : 1;
                var dialValue = ComputeDialValue(_unclampedValue, f);
                if ( (!_clampMin || dialValue >= _min) && (!_clampMax || dialValue <= _max) )
                {
                    DrawTick(f, color, lineThickness);
                }
            }
        }

        private static float GetDialRatio(Vector2 mousePos)
        {
            var p = mousePos - _center;
            return (float)Math.Round(MathUtils.Fmod(-Math.Atan2(p.X, p.Y) / (Math.PI * 2), 1) * 100) / 100;
        }

        private static bool HandleInteraction()
        {
            if (!IsActive)
                return false;

            var pNow = _io.MousePos;
                
            var dialRatio = GetDialRatio(pNow);

            if (_state == States.WaitingInNeutral)
            {
                _state = States.Manipulating;
                if (dialRatio > 0.75f && _originalValue >= 0)
                {
                    var ratioForOriginalValue = _originalValue / Pow;
                    if (ratioForOriginalValue < 0.5f)
                    {
                        _unclampedValue = ComputeDialValue(_originalValue, dialRatio - 1);
                        return true;
                    }
                }
            }

            var oldValue = _unclampedValue;
            var dialedValue = ComputeDialValue(_unclampedValue, dialRatio);

            var oldValueRatio = oldValue / Pow;
            var newValueRatio = dialedValue / Pow;
            var valueRatioDelta = oldValueRatio - newValueRatio;
            if (valueRatioDelta > 0.75f)
            {
                dialedValue += Pow;
            }
            else if (valueRatioDelta < -0.75f)
            {
                dialedValue -= Pow;
            }
            _unclampedValue = dialedValue;
            //Log.Debug($"F {lastDialRatio:0.00} -> {dialRatio:0.00}    v:{oldValue:G5}->{_unclampedValue:G5}  ratioD: {valueRatioDelta:G4}     dialDelta:{delta:G4}           offset:{offset:G4}  log:{Log10Scale}");

            return true;
        }

        private static void DrawTick(double f, Color color, int lineThickness)
        {
            var rads = -3.141592 * 2 * (f);
            var d = new Vector2((float)Math.Sin(rads), (float)Math.Cos(rads));
            _drawList.AddLine(_center + (Radius) * d, _center + (Radius + RingWidth) * d, color, lineThickness);
        }



        private static void DrawValueDifference()
        {
            var rads1 = -3.141592 * 2 * (_originalValue + 0.5f - 0.25f);
            var rads2 = -3.141592 * 2 * (_unclampedValue + 0.5f - 0.25f);

            var clamped = (rads1 - rads2).Clamp(-3.1415f * 2, 3.1415f * 2);
            rads2 = rads1 - clamped;
            _drawList.PathArcTo(
                                _center,
                                radius: Radius + RingWidth * 0.01f,
                                a_min: -(float)rads1,
                                a_max: -(float)rads2,
                                num_segments: 64);
            _drawList.PathStroke(_activeSegmentColor, ImDrawFlags.None, 1);
        }

            
        private static void DrawValueLabels()
        {
            var shouldDraw = IsActive || _state == States.WaitingInNeutral && _ringIndex == 0;
            if (!shouldDraw)
                return;
                
            var d = Vector2.UnitY;
            var dialRatio = _state == States.WaitingInNeutral 
                                ?0.5f
                                :GetDialRatio(_io.MousePos);

            var lowerPart = MathUtils.Fmod(_unclampedValue, Pow);
            var roundedUpperPart = _unclampedValue - lowerPart;
            ImGui.PushFont(Fonts.FontLarge);
            const float padding = 10;

            var isOnLeft = dialRatio <= 0.5f;
            const float transitionRange = 0.06f;

            var transition = isOnLeft
                                 ? MathUtils.RemapAndClamp(dialRatio, 0, transitionRange, 1, 0).Clamp(0, 1)
                                 : MathUtils.RemapAndClamp(dialRatio, 1 - transitionRange, 1, 0, 1).Clamp(0, 1);

            {
                var smallerLabel = $"{roundedUpperPart:G5}";
                var size = ImGui.CalcTextSize(smallerLabel);

                var color = isOnLeft
                                ? new Color(1, 1, 1, 1f)
                                : new Color(1, 1, 1,  1-transition);
                var x = isOnLeft
                            ? MathUtils.Lerp(-padding - size.X, -size.X / 2, transition)
                            : MathUtils.Lerp(-padding - size.X, -padding - size.X -padding, transition);

                _drawList.AddText(_center + (Radius + RingWidth / 2) * d + new Vector2(x, 0),
                                  color,
                                  smallerLabel);
            }

            // Right / Upper
            {
                var upperLabel = $"{(roundedUpperPart + Pow):G5}";
                var size = ImGui.CalcTextSize(upperLabel);
                    
                var color = isOnLeft 
                                ? new Color(1, 1, 1, 1-transition)
                                : new Color(1, 1, 1, 1f);
                    
                    
                var x = isOnLeft
                            ? MathUtils.Lerp(padding , padding + size.X, transition)
                            : MathUtils.Lerp(padding, -size.X / 2, transition);
                _drawList.AddText(_center + (Radius + RingWidth / 2) * d + new Vector2(x, 0),
                                  color,
                                  upperLabel);
            }

            ImGui.PopFont();
        }
            
        private static double ComputeDialValue(double value, double dialRatio)
        {
            var pow = Math.Pow(10, Log10Scale - 1);
            var lowerPart = MathUtils.Fmod(value, pow);
            var roundedUpperPart = value - lowerPart;
            var newLowerPart = pow * dialRatio;
            var dialedValue = roundedUpperPart + newLowerPart;
            return dialedValue;
        }

        private static bool IsOuterSegment => _ringIndex == RingCount - 1;
        private static float Pow => (float)Math.Pow(10, Log10Scale - 1);
        private static int _ringIndex;
        private static float Log10Scale => AdjustedBaseLog10Scale + _ringIndex;
        private static float Radius => NeutralRadius + _ringIndex * RingWidth;

        private static bool IsActive =>
            (DistanceToCenter > Radius && DistanceToCenter < Radius + RingWidth) ||
            (IsOuterSegment && DistanceToCenter > Radius + RingWidth);

        private const float RingWidth = 100;
        private const float Padding = 3;
    }

    // private static float AnimateTowards(ref float value, float target, float speed = 1)
    // {
    //     var direction = target - value;
    //     if (!(Math.Abs(direction) > 0.00001))
    //         return value;
    //     
    //     var offset = speed * ImGui.GetIO().DeltaTime;
    //     value = direction > 0
    //                 ? Math.Min(target, value + offset)
    //                 : Math.Max(target, value - offset);
    //     return value;
    // }
}