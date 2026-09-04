using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Utils;
using T3.Editor.Gui.Styling;

namespace T3.Editor.Gui.Interaction;

/// <summary>
/// Draws a circular dial to manipulate values with various speeds.
///
/// The manipulation area centers left and right of the current mouse position
/// on the ring -- with a value jump on the opposite side.
/// 
/// </summary>
/// <remarks>
/// Terminology
/// range - normalized angle from -0.5 ... 0.5 with 0 at current value
/// valueRange - delta value for complete revolution of current dial
/// tickInterval = Log10 delta vale between ticks.
/// </remarks>
public static class RadialSliderOverlay
{
    public static void Draw(ref double roundedValue, bool restarted, Vector2 center, double min = double.NegativeInfinity,
                            double max = double.PositiveInfinity,
                            float scale = 0.1f, 
                            bool clampMin = false,  // Todo Implement 
                            bool clampMax= false)
    {
        var drawList = ImGui.GetForegroundDrawList();
        _io = ImGui.GetIO();
            
        if (restarted)
        {
            _value = roundedValue;
            //_mousePositions.Clear();
            _center = _io.MousePos;
            _dampedRadius = 50;
            _dampedAngleVelocity = 0;
            _dampedModifierScaleFactor = 1;
            _lastValueAngle = 0;
            _framesSinceLastMove = 120;
            _originalValue = roundedValue;
            _framesSinceStart = 0;
        }


        //_mousePositions.Add(_io.MousePos);
        // if (_mousePositions.Count > 100)
        // {
        //     _mousePositions.RemoveAt(0);
        // }
        _framesSinceStart++;
        //
        // if (_framesSinceStart <= 1)
        //     return;
            
        var p1 = _io.MousePos;
        var mousePosRadius = Vector2.Distance(_center, p1);
                
        // Update angle...
        var dir = p1 - _center;
        var mouseAngle = MathF.Atan2(dir.X, dir.Y);
        var deltaAngle = DeltaAngle(mouseAngle, _lastValueAngle);
        _lastValueAngle = mouseAngle;

        var hasMoved = Math.Abs(deltaAngle) > 0.015f;
        if (hasMoved)
        {
                    
            _framesSinceLastMove = 0;
        }
        else
        {
            //if(Math.Abs(mousePosRadius - _dampedRadius) > 40) 
            _framesSinceLastMove++;
        }
                
        _dampedAngleVelocity = MathUtils.Lerp(_dampedAngleVelocity, (float)deltaAngle, 0.06f);
                
        // Update radius and value range
        const float maxRadius = 2500;
        var angleDampingFromDelay = (_framesSinceStart > 30 ? MathF.Pow(MathUtils.SmootherStep(2, 15, _framesSinceLastMove),4) : 1) * 0.07f;
        var angleDampingFromRadiusDistance = MathF.Pow(MathUtils.SmootherStep(40, 70, Math.Abs(mousePosRadius - _dampedRadius)), 4);
        _dampedRadius = MathUtils.Lerp(_dampedRadius, mousePosRadius.Clamp(40f,maxRadius), angleDampingFromDelay * angleDampingFromRadiusDistance);
        var normalizedClampedRadius = ( _dampedRadius/1000).Clamp(0.0f, 1);
            
        // Value range and tick interval 
        _dampedModifierScaleFactor = MathUtils.Lerp(_dampedModifierScaleFactor, GetKeyboardScaleFactor(), 0.1f);
        var valueRange = (Math.Pow(3 * (normalizedClampedRadius ), 3)) * 50 * scale * _dampedModifierScaleFactor;
        var tickInterval =  Math.Pow(10, (int)Math.Log10(valueRange * 250 / _dampedRadius) - 2) ;
                
                
        // Update value...
        _value += deltaAngle / (Math.PI * 2) * valueRange;
        roundedValue = _io.KeyCtrl ? _value : Math.Round(_value / (tickInterval)) * tickInterval;
                
        var numberOfTicks = valueRange / tickInterval;
        var anglePerTick = 2*Math.PI / numberOfTicks;
                
        var valueTickOffsetFactor =  MathUtils.Fmod(_value, tickInterval) / tickInterval;
        var tickRatioAlignmentAngle = anglePerTick * valueTickOffsetFactor ;
                
        drawList.AddCircle(_center, _dampedRadius+25,  UiColors.BackgroundFull.Fade(0.1f), 128, 50);
            
        // Draw ticks with labels
        for (int tickIndex = -(int)numberOfTicks/2; tickIndex < numberOfTicks/2; tickIndex++)
        {
            var f = MathF.Pow(MathF.Abs(tickIndex / ((float)numberOfTicks/2)), 0.5f);
            var negF = 1 - f;
            var tickAngle = tickIndex * anglePerTick - mouseAngle - tickRatioAlignmentAngle ;
            var direction = new Vector2(MathF.Sin(-(float)tickAngle), MathF.Cos(-(float)tickAngle));
            var valueAtTick = _value + (tickIndex * anglePerTick - tickRatioAlignmentAngle) / (2 * Math.PI) * valueRange;
            var isPrimary =   Math.Abs(MathUtils.Fmod(valueAtTick + tickInterval * 5, tickInterval * 10) - tickInterval * 5) < tickInterval / 10;
            var isPrimary2 =   Math.Abs(MathUtils.Fmod(valueAtTick + tickInterval * 50, tickInterval * 100) - tickInterval * 50) < tickInterval / 100;
                    
            drawList.AddLine(direction * _dampedRadius + _center,
                             direction * (_dampedRadius + (isPrimary ? 10 : 5f)) + _center,
                             UiColors.ForegroundFull.Fade(negF * (isPrimary ? 1 : 0.5f)),
                             1
                            );

            if (!isPrimary)
                continue;
                
            var font = isPrimary2 ? Fonts.FontBold : Fonts.FontSmall;
            var v = Math.Abs(valueAtTick) < 0.0001 ? 0 : valueAtTick;
            var label = $"{v:G5}";
                        
            ImGui.PushFont(font);
            var size = ImGui.CalcTextSize(label);
            ImGui.PopFont();
                        
            drawList.AddText(font, 
                             font.FontSize, 
                             direction * (_dampedRadius + 30) + _center - size/2, 
                             UiColors.ForegroundFull.Fade(negF * (isPrimary2 ? 1 : 0.3f)), 
                             label);
        }
                
        // Draw previous value
        {
            var visible = IsValueVisible(_originalValue, valueRange);
            if (visible)
            {
                var originalValueAngle= ComputeAngleForValue(_originalValue, valueRange, mouseAngle);
                var direction = new Vector2(MathF.Sin(originalValueAngle), MathF.Cos(originalValueAngle));
                drawList.AddLine(direction * _dampedRadius + _center,
                                 direction * (_dampedRadius - 10) + _center,
                                 UiColors.StatusActivated.Fade(0.8f),
                                 2
                                );
            }
        }
                
        // Draw Value range
        {
            var rangeMin = _value - valueRange * 0.45f;
            var rangeMax = _value + valueRange * 0.45f;
            if (!(min < rangeMin && max < rangeMin || (min > rangeMax && max > rangeMax)))
            {
                const float innerOffset = 4;
                var minAngle= ComputeAngleForValue(Math.Max(min, rangeMin), valueRange, mouseAngle);
                var maxAngle= ComputeAngleForValue(Math.Min(max, rangeMax), valueRange, mouseAngle);
                        
                drawList.PathClear();
                var minVisible = GetDirectionForValueIfVisible(min, valueRange, mouseAngle, out var dirForMinIndicator);
                if (minVisible)
                {
                    drawList.PathLineTo(_center + dirForMinIndicator * (_dampedRadius - 10));
                }
                        
                drawList.PathArcTo(_center, _dampedRadius-innerOffset, -minAngle + MathF.PI/2 , -maxAngle + MathF.PI/2, 90);

                var maxVisible = GetDirectionForValueIfVisible(max, valueRange, mouseAngle, out var dirForMaxIndicator);
                if (maxVisible)
                {
                    drawList.PathLineTo(_center + dirForMaxIndicator * (_dampedRadius - 10));
                }
                drawList.PathStroke(UiColors.ForegroundFull.Fade(0.4f), ImDrawFlags.None, 2);

                if (!minVisible)
                {
                    drawList.PathClear();
                    drawList.PathArcTo(_center, _dampedRadius -innerOffset , -minAngle + MathF.PI/2 -  5 * MathUtils.ToRad , -minAngle + MathF.PI/2, 90);
                    drawList.PathStroke(UiColors.ForegroundFull.Fade(0.3f), ImDrawFlags.None, 2);
                }
                if (!maxVisible)
                {
                    drawList.PathClear();
                    drawList.PathArcTo(_center, _dampedRadius - innerOffset, -maxAngle + MathF.PI/2 +  5 * MathUtils.ToRad , -maxAngle + MathF.PI/2, 90);
                    drawList.PathStroke(UiColors.ForegroundFull.Fade(0.2f), ImDrawFlags.None, 2);
                }
            }
        }

        // Current value at mouse
        {
            var dialFade = MathUtils.SmootherStep(60, 160, _dampedRadius);
            var dialAngle= (float)( (_value - roundedValue) * (2 * Math.PI) / valueRange + mouseAngle);
            _dampedDialValueAngle = MathUtils.LerpRadianAngle(_dampedDialValueAngle, dialAngle, 0.4f);
            var direction = new Vector2(MathF.Sin(_dampedDialValueAngle), MathF.Cos(_dampedDialValueAngle));
            drawList.AddLine(direction * _dampedRadius + _center,
                             direction * (_dampedRadius + 30) + _center,
                             UiColors.ForegroundFull.Fade(0.7f * dialFade),
                             2
                            );
                    
            var labelFade = MathUtils.SmootherStep(200, 300, _dampedRadius);
                
            drawList.AddText(Fonts.FontBold,
                             Fonts.FontBold.FontSize,
                             direction * (_dampedRadius - 40) + _center +  new Vector2(-15,-Fonts.FontSmall.FontSize/2), 
                             Color.White.Fade(labelFade * dialFade), 
                             $"{roundedValue:0.00}\n" 
                            );
        }
    }

    private static bool IsValueVisible(double value, double valueRange)
    {
        return Math.Abs(_value - value) < valueRange * 0.45f;
    }

    private static float ComputeAngleForValue(double value, double valueRange, float mouseAngle)
    {
        return (float)( (_value - value) * (2 * Math.PI) / valueRange + mouseAngle);
    }

    private static bool GetDirectionForValueIfVisible(double value, double valueRange, float mouseAngle, out Vector2 direction)
    {
        direction = Vector2.Zero;
            
        if(!IsValueVisible(value, valueRange))
            return false;

        var angle = ComputeAngleForValue(value, valueRange, mouseAngle);
        direction = new Vector2(MathF.Sin(angle), MathF.Cos(angle));
        return true;
    }


    private static double DeltaAngle(double angle1, double angle2)
    {
        angle1 = (angle1 + Math.PI) % (2 * Math.PI) - Math.PI;
        angle2 = (angle2 + Math.PI) % (2 * Math.PI) - Math.PI;

        var delta = angle2 - angle1;
        return delta switch
                   {
                       > Math.PI  => delta - 2 * Math.PI,
                       < -Math.PI => delta + 2 * Math.PI,
                       _          => delta
                   };
    }
        

    private static bool CalculateIntersection(Vector2 p1A, Vector2 p1B, Vector2 p2A, Vector2 p2B, out Vector2 intersection)
    {
        double a1 = p1B.Y - p1A.Y;
        double b1 = p1A.X - p1B.X;
        double c1 = a1 * p1A.X + b1 * p1A.Y;

        double a2 = p2B.Y - p2A.Y;
        double b2 = p2A.X - p2B.X;
        double c2 = a2 * p2A.X + b2 * p2A.Y;

        double determinant = a1 * b2 - a2 * b1;

        if (Math.Abs(determinant) < 1e-6) // Lines are parallel
        {
            intersection =Vector2.Zero;
            return false;
        }

        var x = (b2 * c1 - b1 * c2) / determinant;
        var y = (a1 * c2 - a2 * c1) / determinant;
        intersection = new Vector2((float)x, (float)y);
        return true;
    }
        

    private static double GetKeyboardScaleFactor()
    {
        if (_io.KeyAlt)
            return 10;

        if (_io.KeyShift)
            return 0.1;

        return 1;
    }
        
    /** The precise value before rounding. This used for all internal calculations. */
    private static double _value; 

    private static float _dampedRadius;
    private static Vector2 _center = Vector2.Zero;
    private static float _dampedAngleVelocity;
    private static double _lastValueAngle;
    private static double _dampedModifierScaleFactor;
    private static float _dampedDialValueAngle;
    private static int _framesSinceLastMove;
    private static double _originalValue;
    //private static readonly List<Vector2> _mousePositions = new(100);
    //private static ImDrawListPtr _drawList;
    private static ImGuiIOPtr _io;
    private static int _framesSinceStart;
}