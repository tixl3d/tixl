using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Utils;
using T3.Editor.Gui.Styling;

namespace T3.Editor.Gui.UiHelpers;

/// <summary>
/// Draws vertical-first connections by rotating the proven horizontal algorithm by +90°.
/// Assumes pins start from top/bottom center of their nodes.
/// </summary>
internal static class VerticalConnectionDrawer
{
    private const float Pi = (float)System.Math.PI;

    internal static bool DrawConnection(float canvasScale,
                                        ImRect sourceNode, Vector2 sourcePos,
                                        ImRect targetNode, Vector2 targetPos,
                                        Color color, float thickness,
                                        out Vector2 hoverPosition, out float normalizedHoverPos)
    {
        var s = canvasScale.Clamp(0.2f, 2f);
        hoverPosition = Vector2.Zero;
        normalizedHoverPos = -1;

        sourcePos += Vector2.One * 0.5f;
        targetPos += Vector2.One * 0.5f;

        var debArcOffset = MathF.Sin((float)ImGui.GetTime() * 5) * 0.5f;
        var debRadiusOffset = MathF.Sin((float)ImGui.GetTime() * 5) * 3f;

        
        var vis = sourceNode; vis.Add(targetNode); vis.Add(sourcePos); vis.Add(targetPos);
        if (!ImGui.IsRectVisible(vis.Min, vis.Max))
            return false;

        var dl = ImGui.GetWindowDrawList();
        dl.PathClear();

        var dx = targetPos.X - sourcePos.X;
        var dy = targetPos.Y - sourcePos.Y;

        // trivial straight vertical
        if (dy > 0 && MathF.Abs(dx) < 2)
        {
            dl.PathLineTo(sourcePos);
            dl.PathLineTo(targetPos);
            return FinalizeStroke(dl, s, color, thickness,
                                  out hoverPosition, out normalizedHoverPos);
        }

        var fallbackRectSize = new Vector2(120, 50) * s;
        if (sourceNode.GetWidth() < 1)
        {
            var rectAMin = new Vector2(sourcePos.X - fallbackRectSize.X / 2, sourcePos.Y - fallbackRectSize.Y);
            sourceNode = new ImRect(rectAMin, rectAMin + fallbackRectSize);
        }
        if (targetNode.GetWidth() < 1)
        {
            var rectBMin = new Vector2(targetPos.X - fallbackRectSize.X / 2, targetPos.Y);
            targetNode = new ImRect(rectBMin, rectBMin + fallbackRectSize);
        }

        var sourceLeftOfTarget = sourcePos.X < targetPos.X;

        // radius = horizontal clearance
        var scR = sourceLeftOfTarget ? sourceNode.Max.X - sourcePos.X : sourcePos.X - sourceNode.Min.X;
        
        var tcR = sourceLeftOfTarget ? targetPos.X - targetNode.Min.X + 10 : targetNode.Max.X - targetPos.X;

        const float verticalCompress = 0.2f;
        var minRadius = (dx > 0 ? 5 : 3) * s;

        scR = scR * verticalCompress + minRadius;
        tcR = tcR * verticalCompress + minRadius;

        var possibleSourceRadius = dy - tcR;
        var clampedSourceRadius = MathF.Min(possibleSourceRadius, UserSettings.Config.MaxCurveRadius * s);
        scR = MathF.Max(scR, clampedSourceRadius);

        var d = new Vector2(dx, dy).Length();
        scR = MathF.Min(scR, d / 4f);
        tcR = MathF.Min(tcR, d / 4f);

        var normalRadius = tcR;
        var tightRadius = MathF.Min(tcR, MathF.Abs(dx) * 0.1f);
        tcR = dy.RemapAndClamp(-400 * s, 20 * s, tightRadius, normalRadius);

        var sumR = scR + tcR;

        var scY = sourcePos.Y + dy - tcR - scR;
        if (dy < sumR)
            scY = sourcePos.Y;

        var tcY = targetPos.Y;
        float scX, tcX;
        if (sourceLeftOfTarget)
        {
            scX = sourcePos.X + scR;
            tcX = targetPos.X - tcR;
        }
        else
        {
            scX = sourcePos.X - scR;
            tcX = targetPos.X + tcR;
        }

        var sc = new Vector2(scX, scY);
        var tc = new Vector2(tcX, tcY);


        
        float startAngleSc, endAngleSc;
        float startAngleTc, endAngleTc;

        if (scY > sourcePos.Y)
        {
            dl.PathLineTo(sourcePos);
        }

        if (dy >= sumR && MathF.Abs(dx) > sumR)
        {
            if (sourceLeftOfTarget)
            {
                startAngleSc = 1f * Pi;       
                endAngleSc = 0.5f * Pi;
                
                startAngleTc = -0.5f * Pi; 
                endAngleTc = 0 * Pi;
                
            }
            else
            {
                startAngleSc = 0;      
                endAngleSc = 0.5f * Pi;
                
                startAngleTc = 1.5f * Pi;
                endAngleTc = 1f * Pi;
            }
        }
        else
        {

            
            var distanceBetweenCenters = Vector2.Distance(sc, tc);
            if (distanceBetweenCenters < Math.Abs(scR - tcR))
            {
                dl.PathLineTo(targetPos);
                return FinalizeStroke(dl, s, color, thickness,
                                      out hoverPosition, out normalizedHoverPos);
            }

            if (MathF.Abs(dx) < sumR)
            {
                
                var flipped = scX < tcX;
                if (dy < 0) flipped = !flipped;

                var angleAdjustment = ComputeInnerTangentAngle(sc, scR - 1, tc, tcR, flipped);
                if (sourceLeftOfTarget)
                {
                    
                    startAngleSc =  Pi; 
                    endAngleSc = -0f * Pi + angleAdjustment;
                    startAngleTc = -1f * Pi + angleAdjustment; 
                    endAngleTc = 0f * Pi;
                }
                else
                {
                    startAngleSc = 0; 
                    endAngleSc = 0f * Pi + angleAdjustment;
                    startAngleTc = 1f * Pi + angleAdjustment; 
                    endAngleTc = 1f * Pi;
                }
            }
            else
            {
                
                var flipped = scY - scR <= tcY + tcR;
                if (dx < 0 && scY > tcY + tcR + scR) 
                    flipped = !flipped;
                
                var angleAdjustment = ComputeInnerTangentAngle(sc, scR, tc, tcR, flipped);
                if (sourceLeftOfTarget)
                {
                    
                    startAngleSc = 1f * Pi; 
                    endAngleSc = 0.5f * Pi + angleAdjustment;
                    startAngleTc = 1.5f * Pi + angleAdjustment; 
                    endAngleTc = 2f * Pi;
                }
                else
                {
                    if (flipped)
                    {
                        // scR += debRadiusOffset;
                        // tcR += debRadiusOffset;
                        startAngleSc = 0f* Pi; 
                        endAngleSc = 0f * Pi + angleAdjustment;
                        startAngleTc = 1f * Pi + angleAdjustment; 
                        endAngleTc = 1f * Pi;                        
                    }
                    else
                    {
                        startAngleSc = 0f* Pi; 
                        endAngleSc = 1f * Pi + angleAdjustment;
                        startAngleTc = 2f * Pi + angleAdjustment; 
                        endAngleTc = 1f * Pi;                        
                    }
                }
            }
        }

        

        
        var segS = SegmentCount(MathF.Abs(startAngleSc - endAngleSc), s);
        dl.PathArcTo(sc, scR, startAngleSc, endAngleSc, segS);

        var segT = SegmentCount(MathF.Abs(startAngleTc - endAngleTc), s);
        dl.PathArcTo(tc, tcR, startAngleTc, endAngleTc, segT);

        //dl.PathLineTo(targetPos);

        return FinalizeStroke(dl, s, color, thickness,
                              out hoverPosition, out normalizedHoverPos);
    }

    private static bool FinalizeStroke(ImDrawListPtr dl, float s, Color color, float thickness,
                                       out Vector2 hoverPosition, out float normalizedHoverPos)
    {
        var hovering = LegacyConnectionDrawer.TestHoverDrawListPath(ref dl, out hoverPosition, out normalizedHoverPos);

        if (s > 0.5f)
        {
            dl.AddPolyline(ref dl._Path[0], dl._Path.Size,
                           UiColors.WindowBackground.Fade(0.6f * color.A),
                           ImDrawFlags.None, thickness + 5f);
        }

        dl.PathStroke(color, ImDrawFlags.None, thickness);
        return hovering;
    }

    private static int SegmentCount(float arcLenRad, float scale)
    {
        var circleResolution = (int)scale.RemapAndClamp(0.2f, 1.5f, 6, 15);
        return (int)(arcLenRad * circleResolution).Clamp(1, UserSettings.Config.MaxSegmentCount);
    }

    private static float ComputeInnerTangentAngle(Vector2 centerA, float radiusA,
                                                  Vector2 centerB, float radiusB,
                                                  bool flipped = false)
    {
        var dx = centerB.X - centerA.X;
        var dy = centerB.Y - centerA.Y;
        var d = MathF.Sqrt(dx * dx + dy * dy);
        if (d <= MathF.Abs(radiusA - radiusB))
        {
            return 0;
        }

        var thetaBase = MathF.Atan2(dy, dx);
        var thetaOffset = MathF.Acos((radiusA + radiusB) / d);
        var a1 = Normalize(thetaBase + thetaOffset);
        var a2 = Normalize(thetaBase - thetaOffset);
        
        return flipped ? (a2 < 0 ? a1 : a2) 
                   : (a2 > 0 ? a1 : a2);
    }

    private static float Normalize(float a)
    {
        while (a <= -Pi) a += 2 * Pi;
        while (a > Pi) a -= 2 * Pi;
        return a;
    }
}

