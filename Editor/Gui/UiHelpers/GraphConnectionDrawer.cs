using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Utils;
using T3.Editor.Gui.Styling;

namespace T3.Editor.Gui.UiHelpers;

internal static class GraphConnectionDrawer
{
    private const float Pi = (float)Math.PI;

    /// <summary>
    /// Returns true if hovering...
    /// </summary>
    internal static bool DrawConnection(float canvasScale, ImRect Sn, Vector2 Sp,
                                        ImRect Tn, Vector2 Tp, Color color, float thickness,
                                        out Vector2 hoverPosition, out float normalizedHoverPos)
    {
        var currentCanvasScale = canvasScale.Clamp(0.2f, 2f);
        hoverPosition = Vector2.Zero;
        normalizedHoverPos = -1;

        Sp += Vector2.One *0.5f;
        Tp +=  Vector2.One *0.5f;
        // Early out if not visible
        var r2 = Sn;
        r2.Add(Tn);
        r2.Add(Tp);
        r2.Add(Sp);

        if (!ImGui.IsRectVisible(r2.Min, r2.Max))
            return false;

        var drawList = ImGui.GetWindowDrawList();
        drawList.PathClear();

        var dx = Tp.X - Sp.X;
        var dy = Tp.Y - Sp.Y;

        if (dx > 0 && MathF.Abs(Tp.Y - Sp.Y) < 2)
        {
            drawList.PathLineTo(Sp); // Start at source point
            drawList.PathLineTo(Tp); // Start at source point
        }
        else
        {
            // Ensure rects have valid sizes
            var fallbackRectSize = new Vector2(120, 50) * currentCanvasScale;
            if (Sn.GetHeight() < 1)
            {
                var rectAMin = new Vector2(Sp.X - fallbackRectSize.X, Sp.Y - fallbackRectSize.Y / 2);
                Sn = new ImRect(rectAMin, rectAMin + fallbackRectSize);
            }

            if (Tn.GetHeight() < 1)
            {
                var rectBMin = new Vector2(Tp.X, Tp.Y - fallbackRectSize.Y / 2);
                Tn = new ImRect(rectBMin, rectBMin + fallbackRectSize);
            }

            var sourceAboveTarget = Sp.Y < Tp.Y;

            // Determine radii
            var Sc_r = sourceAboveTarget
                           ? Sn.Max.Y - Sp.Y // Distance to bottom of source node
                           : Sp.Y - Sn.Min.Y; // Distance to top of source node

            var Tc_r = sourceAboveTarget
                           ? Tp.Y - Tn.Min.Y + 10 // Distance from target point to top of target node
                           // plus a small offset to avoid lines from top and bottom falling together
                           : Tn.Max.Y - Tp.Y; // Distance from target point to bottom of target node

            const float horizontalCompress = 0.2f;
            
            
            var minRadius = (dy > 0 ? 5: 3) * canvasScale;

            // Compress packing towards input stacks...
            Tc_r *= horizontalCompress;
            Tc_r += minRadius;

            Sc_r *= horizontalCompress;
            Sc_r += minRadius;

            var possibleSourceRadius = dx - Tc_r;
            var clampedSourceRadius = MathF.Min(possibleSourceRadius, UserSettings.Config.MaxCurveRadius * canvasScale);
            Sc_r = MathF.Max(Sc_r, clampedSourceRadius);

            var d = new Vector2(dx, dy).Length();
            Sc_r = MathF.Min(Sc_r,d / 4f);
            Tc_r = MathF.Min(Tc_r,d / 4f);

            // Use smaller wrap radius for back connections
            var normalRadius = Tc_r;
            var tightRadius = MathF.Min(Tc_r, MathF.Abs(dy) * 0.1f);
            Tc_r = MathUtils.RemapAndClamp(dx, -400 * canvasScale, 20 * canvasScale,tightRadius, normalRadius );
            
            var sumR = Sc_r + Tc_r;

            // Adjust Sc.x to be further left by Sc_r
            var Sc_x = Sp.X + dx - Tc_r - Sc_r;
            if (dx < sumR)
            {
                // If horizontal space is too small, adjust Sc_x to Sp.X
                Sc_x = Sp.X;
            }

            var Tc_x = Tp.X;
            float Sc_y, Tc_y;

            if (sourceAboveTarget)
            {
                Sc_y = Sp.Y + Sc_r;
                Tc_y = Tp.Y - Tc_r;
            }
            else
            {
                Sc_y = Sp.Y - Sc_r;
                Tc_y = Tp.Y + Tc_r;
            }

            var Sc = new Vector2(Sc_x, Sc_y);
            var Tc = new Vector2(Tc_x, Tc_y);

            // Debug viz
            // drawList.AddCircle(Sc, Sc_r, Color.Orange.Fade(0.1f));
            // drawList.AddCircle(Tc, Tc_r, Color.Orange.Fade(0.1f));

            // Determine angles for arcs
            float startAngle_Sc, endAngle_Sc;
            float startAngle_Tc, endAngle_Tc;

            if(Sc_x > Sp.X)
                drawList.PathLineTo(Sp); // Start at source point

            if (dx >= sumR && MathF.Abs(dy) > sumR)
            {
                // Ideal case, use fixed angles
                if (sourceAboveTarget)
                {
                    // Source Arc from 270 degrees to 360 degrees
                    startAngle_Sc = 1.5f * Pi;
                    endAngle_Sc = 2f * Pi;

                    // Target Arc from 180 degrees to 90 degrees
                    startAngle_Tc = Pi;
                    endAngle_Tc = 0.5f * Pi;
                }
                else
                {
                    // Source Arc from 90 degrees to 0 degrees
                    startAngle_Sc = 0.5f * Pi;
                    endAngle_Sc = 0f;

                    // Target Arc from 180 degrees to 270 degrees
                    startAngle_Tc = Pi;
                    endAngle_Tc = 1.5f * Pi;
                }
            }
            else
            {
                var distanceBetweenCenters = Vector2.Distance(Sc, Tc);
                if (distanceBetweenCenters < Math.Abs(Sc_r - Tc_r))
                {
                    // Circles are overlapping; draw a straight line for simplicity
                    drawList.PathLineTo(Tp);
                    drawList.PathStroke(color, ImDrawFlags.None, thickness);
                    return false;
                }

                if (MathF.Abs(dy) < sumR)
                {
                    // Vertical space is too small, adjust the start and end angles
                    var flipped = Sc_y > Tc_y;
                    if (dx < 0)
                        flipped = !flipped;

                    var angleAdjustment = ComputeInnerTangentAngle(Sc, Sc_r - 1, Tc, Tc_r, flipped);
                    if (sourceAboveTarget)
                    {
                        // Adjust angles for source arc
                        startAngle_Sc = 1.5f * Pi;
                        endAngle_Sc = 2 * Pi + angleAdjustment;

                        // Adjust angles for target arc
                        startAngle_Tc = 1f * Pi + angleAdjustment;
                        endAngle_Tc = 0.5f * Pi;
                    }
                    else
                    {
                        // Adjust angles for source arc
                        startAngle_Sc = 0.5f * Pi;
                        endAngle_Sc = +angleAdjustment;

                        // Adjust angles for target arc
                        startAngle_Tc = Pi + angleAdjustment;
                        endAngle_Tc = 1.5f * Pi;
                    }
                }
                else
                {
                    // Horizontal space is too small, adjust the start and end angles
                    var flipped = Sc_x - Sc_r <= Tc_x + Tc_r;
                    if (dy < 0 && Sc_x > Tc_x + Tc_r + Sc_r)
                        flipped = !flipped;

                    //Tc += Vector2.One * MathF.Sin((float)ImGui.GetTime() * 10);

                    var angleAdjustment = ComputeInnerTangentAngle(Sc, Sc_r, Tc, Tc_r, flipped);
                    if (sourceAboveTarget)
                    {
                        // Adjust angles for source arc
                        startAngle_Sc = 1.5f * Pi;
                        endAngle_Sc = 2 * Pi + angleAdjustment;

                        // Adjust angles for target arc
                        startAngle_Tc = 1f * Pi + angleAdjustment;
                        endAngle_Tc = 0.5f * Pi;
                    }
                    else
                    {
                        // Adjust angles for source arc
                        startAngle_Sc = 0.5f * Pi;
                        endAngle_Sc = +angleAdjustment;

                        // Adjust angles for target arc
                        startAngle_Tc = Pi + angleAdjustment;
                        endAngle_Tc = 1.5f * Pi;
                    }
                }
            }
            
            var segments = ComputerSegmentCount(MathF.Abs(startAngle_Sc - endAngle_Sc), canvasScale);
            drawList.PathArcTo(Sc, Sc_r, startAngle_Sc, endAngle_Sc, segments);

            var segmentsT = ComputerSegmentCount(MathF.Abs(startAngle_Tc - endAngle_Tc), canvasScale);
            drawList.PathArcTo(Tc, Tc_r, startAngle_Tc, endAngle_Tc, segmentsT);
        }

        var isHovering = LegacyConnectionDrawer.TestHoverDrawListPath(ref drawList, out hoverPosition, out  normalizedHoverPos);

        // Optionally draw an outline
        if (currentCanvasScale > 0.5f)
        {
            drawList.AddPolyline(ref drawList._Path[0], drawList._Path.Size, 
                                 UiColors.WindowBackground.Fade(0.6f * color.A), 
                                 ImDrawFlags.None, 
                                 thickness + 5f);
        }

        drawList.PathStroke(color, ImDrawFlags.None, thickness);

        return isHovering;
    }

    private static int ComputerSegmentCount(float arcLengthRad, float canvasScale)
    {
        var circleResolution = (int) canvasScale.RemapAndClamp(0.2f, 1.5f, 6, 15);
        return (int)(arcLengthRad * circleResolution).Clamp(1, UserSettings.Config.MaxSegmentCount);
    }
    
    private static float ComputeInnerTangentAngle(Vector2 centerA, float radiusA, Vector2 centerB, float radiusB, bool flipped = false)
    {
        // Calculate the differences in x and y coordinates
        var deltaX = centerB.X - centerA.X;
        var deltaY = centerB.Y - centerA.Y;

        // Calculate the distance between the centers of the circles
        var d = MathF.Sqrt(deltaX * deltaX + deltaY * deltaY);

        // Check if the inner tangent exists
        if (d <= MathF.Abs(radiusA - radiusB))
        {
            return 0;
        }

        // Base angle between the centers
        var thetaBase = MathF.Atan2(deltaY, deltaX);

        // Angle offset for the inner tangent
        var thetaOffset = MathF.Acos((radiusA + radiusB) / d);

        // Calculate both possible angles of the inner tangents
        var angle1 = NormalizeAngle(thetaBase + thetaOffset);
        var angle2 = NormalizeAngle(thetaBase - thetaOffset);

        // Decide which angle to use based on the position of Circle B relative to Circle A
        float selectedAngle;

        if (flipped)
        {
            // B is below A, choose the angle that points downward
            selectedAngle = (angle1 < 0) ? angle1 : angle2;
        }
        else
        {
            // B is above A, choose the angle that points upward
            selectedAngle = (angle1 > 0) ? angle1 : angle2;
        }

        return selectedAngle;
    }

    private static float NormalizeAngle(float angle)
    {
        while (angle <= -Math.PI) angle += 2 * MathF.PI;
        while (angle > Math.PI) angle -= 2 * MathF.PI;
        return angle;
    }
}