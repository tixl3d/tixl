using T3.Core.Rendering;

namespace Lib.render._dx11.api;

[Guid("c676b9c7-06d7-4ee0-8ffc-9bee96c5dc18")]
internal sealed class DrawInstancedIndirect: Instance<DrawInstancedIndirect>
{
    [Output(Guid = "3A8880AF-BBBF-4560-B0C7-6E643A20FC20", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<Command> Output = new();

    public DrawInstancedIndirect()
    {
        Output.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        Buffer buffer = Buffer.GetValue(context);
        if (buffer == null)
        {
            Log.Warning("Undefined drawArgs buffer", this);
            return;
        }
        
            
        var device = ResourceManager.Device;
        var deviceContext = device.ImmediateContext;
        
        // A compute shader that wrote the args may still have the buffer bound as UAV;
        // unbind so the indirect read doesn't hazard. No Flush - the immediate context
        // already orders the dispatch before this draw, and a Flush stalls the queue.
        deviceContext.ComputeShader.SetUnorderedAccessViews(0, _nullUavs);
        // During a geometry capture pass the draw is replaced by its registration (below): the
        // capture reads buffers, and rasterizing would paint into whatever target is bound.
        if (!context.CaptureOnlyDraws)
            deviceContext.DrawInstancedIndirect(buffer, AlignedByteOffsetForArgs.GetValue(context));

        // While a geometry capture pass is active (e.g. the laser camera rendering its scene),
        // register the geometry wired for capture - see Draw.cs for the same mechanism.
        var capture = context.GeometryCapture;
        if (capture is { IsActive: true })
        {
            var captureVertices = CaptureVertices.GetValue(context);
            if (captureVertices?.Buffer != null
                // Laser-frame data drawn back into the scene is a preview of the camera's own
                // output; capturing it here would feed the camera its last frame (a feedback
                // oscillation that flashes the laser between two states). Same guard as Draw.cs.
                && !GeometryCapture.IsExcludedFromCapture(captureVertices.Buffer))
            {
                var captureIndices = CaptureIndices.GetValue(context);
                GeometryPrimitiveKind kind;
                if (captureIndices?.Buffer != null)
                    kind = GeometryPrimitiveKind.Mesh;
                else
                {
                    kind = CaptureKind.GetValue(context) switch
                    {
                        1 => GeometryPrimitiveKind.PointCloud,
                        2 => GeometryPrimitiveKind.Lines,
                        3 => GeometryPrimitiveKind.Mesh,
                        _ => GeometryPrimitiveKind.Auto,
                    };
                }
                capture.Register(captureVertices.Buffer, captureIndices?.Buffer, kind, context.ObjectToWorld);
            }
        }
    }

    private static readonly UnorderedAccessView[] _nullUavs = new UnorderedAccessView[4];

    [Input(Guid = "6C87816C-DA1D-4429-A1F6-61233AA3D7B1")]
    public readonly InputSlot<Buffer> Buffer = new InputSlot<Buffer>();
    [Input(Guid = "BC874135-45F2-45E2-8005-244B9123ED20")]
    public readonly InputSlot<int> AlignedByteOffsetForArgs = new();

    // Optional geometry for capture passes (e.g. the laser camera) - see Draw.cs
    [Input(Guid = "2772EDF0-962F-40D6-A034-194110B7DA58")]
    public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> CaptureVertices = new();
    [Input(Guid = "D2FE03D7-57C9-444C-94FE-8852527BC2AA")]
    public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> CaptureIndices = new();

    /// <summary>Primitive kind for capture: 0 = auto, 1 = points, 2 = lines, 3 = mesh.</summary>
    [Input(Guid = "0C082078-895B-4D95-901A-75435D310007")]
    public readonly InputSlot<int> CaptureKind = new();
}