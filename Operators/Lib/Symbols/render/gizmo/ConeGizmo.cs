namespace Lib.render.gizmo;

/// <summary>
/// Generates points for drawing a wireframe audio cone visualization matching BASS/ManagedBass 3D audio cone behavior.
/// BASS cones are simple conical shapes defined by full angles (0-360 degrees).
/// The cone extends from the origin in the -Z direction (forward).
/// Generates line segments for: base circle, and radial lines from apex to base.
/// No rounded cap is used as BASS implements a hard cone boundary.
/// 
/// Output points can be fed into DrawLines for rendering.
/// </summary>
[Guid("f7e3c9a4-2b8d-4e6f-a1c5-9d7b3e8f6a2c")]
internal sealed class ConeGizmo : Instance<ConeGizmo>
{
    [Output(Guid = "a8c4e2f7-3d9b-4a6e-8c1f-5b7d9e3a2c8f")]
    public readonly Slot<StructuredList> Points = new();

    public ConeGizmo()
    {
        Points.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var angleDegrees = Angle.GetValue(context);
        var length = Length.GetValue(context);
        var segments = Math.Max(3, Segments.GetValue(context));
        var rayCount = Math.Max(0, RayCount.GetValue(context));

        // Calculate radius at base using BASS cone geometry
        // BASS uses full angle (spread from edge to edge), so we use half for the geometry
        var halfAngleRadians = angleDegrees * 0.5f * MathF.PI / 180f;
        var radius = MathF.Tan(halfAngleRadians) * length;

        // Cone extends in -Z direction (forward)
        var baseZ = -length;
        
        // Generate base circle points (closed loop)
        // Each segment needs 2 points (start and end), plus separator
        var baseCirclePointCount = segments * 3; // 2 points + 1 separator per segment
        
        // Generate ray line points from apex to base
        // Each ray needs 3 points: apex, base point, separator
        var rayPointCount = rayCount * 3;
        
        var totalPoints = baseCirclePointCount + rayPointCount;
        
        if (_pointList == null || _pointList.NumElements != totalPoints)
        {
            _pointList = new StructuredList<Point>(totalPoints);
        }
        
        var points = _pointList.TypedElements;
        var index = 0;
        
        // Generate base circle line segments
        for (var i = 0; i < segments; i++)
        {
            var angle1 = (i / (float)segments) * MathF.PI * 2f;
            var angle2 = ((i + 1) / (float)segments) * MathF.PI * 2f;
            
            var x1 = MathF.Cos(angle1) * radius;
            var y1 = MathF.Sin(angle1) * radius;
            var x2 = MathF.Cos(angle2) * radius;
            var y2 = MathF.Sin(angle2) * radius;
            
            points[index++] = new Point
            {
                Position = new Vector3(x1, y1, baseZ),
                F1 = 1f,
                Color = Vector4.One
            };
            points[index++] = new Point
            {
                Position = new Vector3(x2, y2, baseZ),
                F1 = 1f,
                Color = Vector4.One
            };

            // mark the end of a line/segment so renderers / line builders don't connect across segments.
            points[index++] = Point.Separator();
        }
        
        // Generate ray lines from apex (origin) to base circle
        for (var i = 0; i < rayCount; i++)
        {
            var angle = (i / (float)rayCount) * MathF.PI * 2f;
            var x = MathF.Cos(angle) * radius;
            var y = MathF.Sin(angle) * radius;
            
            // Apex point (origin)
            points[index++] = new Point
            {
                Position = Vector3.Zero,
                F1 = 1f,
                Color = Vector4.One
            };
            // Base point
            points[index++] = new Point
            {
                Position = new Vector3(x, y, baseZ),
                F1 = 1f,
                Color = Vector4.One
            };
            // marks the end of this ray segment (prevents connecting to the next ray)
            points[index++] = Point.Separator();
        }
        
        Points.Value = _pointList;
    }

    private StructuredList<Point> _pointList;

    /// <summary>The full cone angle in degrees (0-360). This is the total spread, not half-angle.</summary>
    [Input(Guid = "b9d5f3a7-4c8e-2f1b-9a6d-7e3c5b8f1a4d")]
    public readonly InputSlot<float> Angle = new();

    /// <summary>The length of the cone visualization.</summary>
    [Input(Guid = "c1e6a8b4-5d7f-3a9c-8e2b-6f4d1c9a7e5b")]
    public readonly InputSlot<float> Length = new();

    /// <summary>Number of segments for the base circle.</summary>
    [Input(Guid = "e3a8c1d6-7f9b-5c2e-a4b8-8d6f3e1a9c7b")]
    public readonly InputSlot<int> Segments = new();

    /// <summary>Number of radial ray lines from apex to base (set to 0 for circle only).</summary>
    [Input(Guid = "a4b5c6d7-8e9f-0a1b-2c3d-4e5f6a7b8c9d")]
    public readonly InputSlot<int> RayCount = new();
}
