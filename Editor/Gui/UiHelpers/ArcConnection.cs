using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Utils;
using T3.Editor.Gui.Styling;

// ReSharper disable InconsistentNaming

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

        var isHovering = ArcConnection.TestHoverDrawListPath(ref drawList, out hoverPosition, out  normalizedHoverPos);

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

internal static class ArcConnection
{
    private static readonly Color OutlineColor = new(0.1f, 0.1f, 0.1f, 0.6f);

    /// <summary>
    /// Draws an arc connection line.
    /// </summary>
    /// <remarks>
    /// Assumes that B is the tight node connection direction is bottom to top
    /// 
    ///                                dx
    ///        +-------------------+
    ///        |      A            +----\  rB+---------------+
    ///        +-------------------+rA   \---+      B        |
    ///                                      +---------------+
    /// </remarks>
    private const float Pi = 3.141592f;

    private const float TAU = Pi / 180;

    public static bool Draw(Vector2 canvasScale, ImRect rectA, Vector2 pointA, ImRect rectB, Vector2 pointB, Color color, float thickness,
                            out Vector2 hoverPosition)
    {
        hoverPosition = Vector2.Zero;
        
        var currentCanvasScale = canvasScale.X.Clamp(0.2f, 2f);
        pointA.X -= (3 - 1 * currentCanvasScale).Clamp(1, 1);
        pointB.X += (4 - 4 * currentCanvasScale).Clamp(0, 4);

        var r2 = rectA;
        r2.Add(rectB);
        r2.Add(pointB);
        r2.Add(pointA);

        if (!ImGui.IsRectVisible(r2.Min, r2.Max))
            return false;

        var drawList = ImGui.GetWindowDrawList();

        var fallbackRectSize = new Vector2(120, 50) * currentCanvasScale;
        if (rectA.GetHeight() < 1)
        {
            var rectAMin = new Vector2(pointA.X - fallbackRectSize.X, pointA.Y - fallbackRectSize.Y / 2);
            rectA = new ImRect(rectAMin, rectAMin + fallbackRectSize);
        }

        if (rectB.GetHeight() < 1)
        {
            var rectBMin = new Vector2(pointB.X, pointB.Y - fallbackRectSize.Y / 2);
            rectB = new ImRect(rectBMin, rectBMin + fallbackRectSize);
        }

        var d = pointB - pointA;
        const float limitArcConnectionRadius = 50;
        var maxRadius = limitArcConnectionRadius * currentCanvasScale * 2f;
        const float shrinkArkAngle = 0.8f;
        var edgeFactor = (0.35f * (currentCanvasScale - 0.3f)).Clamp(0.01f, 0.35f); // 0 -> overlap  ... 1 concentric around node edge
        const float outlineWidth = 3;
        var edgeOffset = 1 * currentCanvasScale;

        var pointAOrg = pointA;

        if (d.Y > -1 && d.Y < 1 && d.X > 2)
        {
            drawList.AddLine(pointA, pointB, color, thickness);
            return false;
        }

        //drawList.PathClear();
        var aAboveB = d.Y > 0;
        if (aAboveB)
        {
            var cB = rectB.Min;
            var rB = (pointB.Y - cB.Y) * edgeFactor + edgeOffset;

            var exceededMaxRadius = d.X - rB > maxRadius;
            if (exceededMaxRadius)
            {
                pointA.X += d.X - maxRadius - rB;
                d.X = maxRadius + rB;
            }

            var cA = rectA.Max;
            var rA = cA.Y - pointA.Y;
            cB.Y = pointB.Y - rB;

            if (d.X > rA + rB)
            {
                var horizontalStretch = d.X / d.Y > 1;
                if (horizontalStretch)
                {
                    var f = d.Y / d.X;
                    var alpha = (float)(f * 90 + Math.Sin(f * 3.1415f) * 8) * TAU;

                    var dt = Math.Sin(alpha) * rB;
                    rA = (float)(1f / Math.Tan(alpha) * (d.X - dt) + d.Y - rB * Math.Sin(alpha));
                    cA = pointA + new Vector2(0, rA);

                    DrawArcTo(drawList, cB, rB, Pi / 2, Pi / 2 + alpha * shrinkArkAngle);
                    DrawArcTo(drawList, cA, rA, 1.5f * Pi + alpha * shrinkArkAngle, 1.5f * Pi);

                    if (exceededMaxRadius)
                        drawList.PathLineTo(pointAOrg);
                }
                else
                {
                    rA = d.X - rB;
                    cA = pointA + new Vector2(0, rA);

                    DrawArcTo(drawList, cB, rB, 0.5f * Pi, Pi);
                    DrawArcTo(drawList, cA, rA, 2 * Pi, 1.5f * Pi);

                    if (exceededMaxRadius)
                        drawList.PathLineTo(pointAOrg);
                }
            }
            else
            {
                FnDrawBezierFallback();
            }
        }
        else
        {
            var cB = new Vector2(rectB.Min.X, rectB.Max.Y);
            var rB = (cB.Y - pointB.Y) * edgeFactor + edgeOffset;

            var exceededMaxRadius = d.X - rB > maxRadius;
            if (exceededMaxRadius)
            {
                pointA.X += d.X - maxRadius - rB;
                d.X = maxRadius + rB;
            }

            var cA = new Vector2(rectA.Max.X, rectA.Min.Y);
            var rA = pointA.Y - cA.Y;
            cB.Y = pointB.Y + rB;

            if (d.X > rA + rB)
            {
                var horizontalStretch = Math.Abs(d.Y) < 2 || -d.X / d.Y > 1;
                if (horizontalStretch)
                {
                    // hack to find angle where circles touch
                    var f = -d.Y / d.X;
                    var alpha = (float)(f * 90 + Math.Sin(f * 3.1415f) * 8f) * TAU;

                    var dt = Math.Sin(alpha) * rB;
                    rA = (float)(1f / Math.Tan(alpha) * (d.X - dt) - d.Y - rB * Math.Sin(alpha));
                    cA = pointA - new Vector2(0, rA);

                    DrawArcTo(drawList, cB, rB, 1.5f * Pi, 1.5f * Pi - alpha * shrinkArkAngle);
                    DrawArcTo(drawList, cA, rA, 0.5f * Pi - alpha * shrinkArkAngle, 0.5f * Pi);

                    if (exceededMaxRadius)
                        drawList.PathLineTo(pointAOrg);
                }
                else
                {
                    rA = d.X - rB;
                    cA = pointA - new Vector2(0, rA);

                    DrawArcTo(drawList, cB, rB, 1.5f * Pi, Pi);
                    DrawArcTo(drawList, cA, rA, 2 * Pi, 2.5f * Pi);

                    if (exceededMaxRadius)
                        drawList.PathLineTo(pointAOrg);
                }
            }
            else
            {
                FnDrawBezierFallback();
            }
        }

        var isHovering = TestHoverDrawListPath(ref drawList, out hoverPosition, out var normalizedLengthPos);
        if (currentCanvasScale > 0.5f)
        {
            drawList.AddPolyline(ref drawList._Path[0], drawList._Path.Size, ColorVariations.OperatorOutline.Apply(color), ImDrawFlags.None,
                                 thickness + outlineWidth);
        }

        drawList.PathStroke(color, ImDrawFlags.None, thickness);
        return isHovering;

        void FnDrawBezierFallback()
        {
            var tangentLength = Vector2.Distance(pointA, pointB).RemapAndClamp(30, 300,
                                                                               5, 200);
            drawList.PathLineTo(pointA);
            drawList.PathBezierCubicCurveTo(pointA + new Vector2(tangentLength, 0),
                                            pointB + new Vector2(-tangentLength, 0),
                                            pointB,
                                            currentCanvasScale < 0.5f ? 11 : 29
                                           );
        }
    }

    private static void DrawArcTo(ImDrawListPtr drawList, Vector2 center, float radius, float angleA, float angleB)
    {
        if (radius < 0.01)
            return;

        drawList.PathArcTo(center, radius, angleA, angleB, radius < 50 ? 4 : 10);
    }

    /// <summary>
    /// Uses the line segments of the path to test for hover intersection.
    /// </summary>
    internal static bool TestHoverDrawListPath(ref ImDrawListPtr drawList,  out Vector2 positionOnLine, out float normalizedLengthPos)
    {
        normalizedLengthPos = 0f;
        var hoverLengthPos = 0f;
        positionOnLine= Vector2.Zero;
        var isHovering = false;
        
        if (drawList._Path.Size < 2)
            return false;

        const float distance = 6;

        var p1 = drawList._Path[0];
        var p2 = drawList._Path[drawList._Path.Size - 1];
        var bounds = new ImRect(p2, p1).MakePositive();

        // A very unfortunate hack to avoid computing the bounds of complex paths.
        // Might not work for all situations...
        var extraPaddingForBackwardsConnection = p1.X > p2.X ? 40 : 0;
        
        bounds.Expand(distance + extraPaddingForBackwardsConnection);
        //foreground.AddRect(bounds.Min, bounds.Max, Color.Orange);
        
        var mousePos = ImGui.GetMousePos();
        if (!bounds.Contains(mousePos))
            return false;

        var totalLength = 0f;   // Track total length of path to compute normalized pos
        var pLast = p1;
        
        // No iterate over all segments
        for (var i = 1; i < drawList._Path.Size; i++)
        {
            var p = drawList._Path[i];
            
            //foreground.AddRect(bounds.Min, bounds.Max, Color.Green.Fade(0.1f));
            
            var v = pLast - p;
            var vLen = v.Length();
            
            bounds = new ImRect(pLast, p).MakePositive();
            bounds.Expand(distance);
            if (bounds.Contains(mousePos))
            {
                //foreground.AddRect(r.Min, r.Max, Color.Orange);

                var d = Vector2.Dot(v, mousePos - p) / vLen;
                positionOnLine = p + v * d / vLen;
                //foreground.AddCircleFilled(positionOnLine, 4f, Color.Red);
                
                if (Vector2.Distance(mousePos, positionOnLine) <= distance)
                {
                    isHovering = true;
                    hoverLengthPos = totalLength + (pLast - mousePos).Length();
                }
            }
            totalLength += vLen;

            pLast = p;
        }

        if (isHovering)
            normalizedLengthPos = hoverLengthPos / totalLength;

        return isHovering;
    }
}