using T3.Core.Rendering;

namespace Lib.render._dx11.api;

[Guid("9b28e6b9-1d1f-42d8-8a9e-33497b1df820")]
internal sealed class Draw : Instance<Draw>, IRenderStatsProvider
{
    [Output(Guid = "49B28DC3-FCD1-4067-BC83-E1CC848AE55C")]
    public readonly Slot<Command> Output = new();

        
    public Draw()
    {
        lock (_lock)
        {
            if (!_registeredForStats)
            {
                RenderStatsCollector.RegisterProvider(this);
                _registeredForStats = true;
            }
        }
        Output.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        //Log.Debug("Draw2", this);
        var vertexCount = VertexCount.GetValue(context);
        var startVertexLocation = VertexStartLocation.GetValue(context);

        var deviceContext = ResourceManager.Device.ImmediateContext;

        var setVs = deviceContext.VertexShader.Shader;
        var setPs = deviceContext.PixelShader.Shader;
            
        if (setVs == null || setPs == null)
        {
            if (!_complainedOnce)
            {
                Log.Warning("Trying to issue draw call, but pixel and/or vertex shader are null.", this);
            }
            _complainedOnce = true;
            return;
        }

        _vertexCount += vertexCount;
        _drawCallCount++;

        _complainedOnce = false;

        // During a geometry capture pass the draw is replaced by its registration (below): the
        // capture reads buffers, and rasterizing would paint into whatever target is bound.
        if (!context.CaptureOnlyDraws)
            deviceContext.Draw(vertexCount, startVertexLocation);

        // While a geometry capture pass is active (e.g. the laser camera rendering its scene),
        // register the geometry wired for capture. This op re-runs every frame even in cached
        // subtrees, so the buffer source can be stale - that's fine, references stay valid.
        var capture = context.GeometryCapture;
        if (capture is { IsActive: true })
        {
            var captureVertices = CaptureVertices.GetValue(context);
            if (captureVertices?.Buffer != null
                // Laser-frame previews (LaserPointsToBuffer output drawn back into the scene)
                // are excluded - capturing them echoes the frame into itself.
                && !GeometryCapture.IsExcludedFromCapture(captureVertices.Buffer))
            {
                var captureIndices = CaptureIndices.GetValue(context);
                GeometryPrimitiveKind kind;
                if (captureIndices?.Buffer != null)
                    kind = GeometryPrimitiveKind.Mesh;
                else
                {
                    // The stock draw graphs declare their primitive kind; Auto falls back to
                    // classifying the buffer content after readback.
                    kind = CaptureKind.GetValue(context) switch
                    {
                        1 => GeometryPrimitiveKind.PointCloud,
                        2 => GeometryPrimitiveKind.Lines,
                        3 => GeometryPrimitiveKind.Mesh,
                        _ => GeometryPrimitiveKind.Auto,
                    };
                }
                capture.Register(captureVertices.Buffer, captureIndices?.Buffer, kind, context.ObjectToWorld,
                                 startVertexLocation, vertexCount, CapturePointSize.GetValue(context));
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        RenderStatsCollector.UnregisterProvider(this);
        base.Dispose(disposing);
    }

    public IEnumerable<(string, int)> GetStats()
    {
        yield return ("Triangles", _vertexCount/3);
        yield return ("Draw calls", _drawCallCount);
    }
        

    public void StartNewFrame()
    {
        _vertexCount = 0;
        _drawCallCount = 0;
    }

    private bool _complainedOnce;
    private static int _vertexCount;
    private static int _drawCallCount;
    private static bool _registeredForStats;
    private static readonly Dictionary<string, int> _statResults = new();
    private static readonly object _lock = new ();
        
    [Input(Guid = "8716B11A-EF71-437E-9930-BB747DA818A7")]
    public readonly InputSlot<int> VertexCount = new();
    [Input(Guid = "B381B3ED-F043-4001-9BBC-3E3915F38235")]
    public readonly InputSlot<int> VertexStartLocation = new();

    // Optional geometry for capture passes (e.g. the laser camera). Wired inside the stock draw
    // graphs; left unconnected the op behaves exactly as before.
    [Input(Guid = "C55AEB0A-BA11-49B8-82EE-2D2A74996C87")]
    public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> CaptureVertices = new();
    [Input(Guid = "E5D1FBD5-DD92-422E-A5FB-D33204421761")]
    public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> CaptureIndices = new();

    /// <summary>Primitive kind the capture should treat this draw as: 0 = auto-classify,
    /// 1 = point cloud, 2 = lines, 3 = mesh. Set by the stock draw graphs.</summary>
    [Input(Guid = "4B524025-F9AF-4AE1-9712-B7C0DCA2723F")]
    public readonly InputSlot<int> CaptureKind = new();

    /// <summary>Draw-time size multiplier the capture consumers apply on top of the buffer's
    /// per-element Scale (e.g. the points op's PointSize uniform). Wired by the stock draw graphs.</summary>
    [Input(Guid = "F68FAFBC-FE56-4987-9DCB-BD3260D5C1AF")]
    public readonly InputSlot<float> CapturePointSize = new(1f);

}