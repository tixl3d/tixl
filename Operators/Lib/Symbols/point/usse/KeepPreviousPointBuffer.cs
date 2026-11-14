#nullable enable
using Lib.Utils;
using T3.Core.Animation;
using T3.Graphics.Compat;

namespace Lib.point.usse;

[Guid("c7752832-7155-4082-acd5-276c1abe7eb4")]
internal sealed class KeepPreviousPointBuffer : Instance<KeepPreviousPointBuffer>
{
    /** Point memory held by all evaluations of one op instance. */
    private sealed class MemoryBudget
    {
        public long AllocatedBytes;
    }

    /** The ping-pong pair of one evaluation within a frame: the copy made this frame and the one before. */
    private sealed class PointHistory(MemoryBudget budget) : IDisposable
    {
        public BufferWithViews? A;
        public BufferWithViews? B;
        public BufferDescription Description;
        public bool Toggle;

        public BufferWithViews? Current => Toggle ? B : A;
        public BufferWithViews? Previous => Toggle ? A : B;

        public void Allocate(BufferDescription description)
        {
            Dispose();
            A = CreateBuffer(description);
            B = CreateBuffer(description);
            Description = description;
            budget.AllocatedBytes += 2L * description.SizeInBytes;
        }

        public void Dispose()
        {
            if (A != null || B != null)
                budget.AllocatedBytes -= 2L * Description.SizeInBytes;

            A?.Dispose();
            B?.Dispose();
            A = null;
            B = null;
            Description = default;
        }
    }

    [Output(Guid = "8c301f66-a743-4c1e-9f2b-fa9cb5904c98")]
    public readonly Slot<BufferWithViews?> BufferA = new();

    [Output(Guid = "74a47202-3c66-476d-a850-fbb2b37dffe5")]
    public readonly Slot<BufferWithViews?> BufferB = new();

    public KeepPreviousPointBuffer()
    {
        BufferA.UpdateAction += UpdateTexture;
        BufferB.UpdateAction += UpdateTexture;
        _history = new EvaluationHistory<PointHistory>(CreatePointHistory, MaxCallsPerPass);
    }

    private void UpdateTexture(EvaluationContext context)
    {
        if (!InputBuffer.HasInputConnections)
            return;

        // Loop iterations, motion blur passes and other windows evaluate this op several times per frame. Each
        // evaluation keeps its own pair, so BufferB is the same evaluation one frame earlier.
        if (!Keep.GetValue(context))
        {
            // Paused time or a repeated motion blur pass: show the matching evaluation of the first pass unchanged.
            if (_history.TryGetFirstPassSlot(context, out var shown) && shown.Current != null)
                SetOutputs(shown.Current, shown.Previous);

            return;
        }

        var src = InputBuffer.GetValue(context);
        if (src?.Buffer == null)
            return;

        var description = src.Buffer.Description;
        if ((description.OptionFlags & ResourceOptionFlags.BufferStructured) == 0 || description.StructureByteStride == 0)
        {
            Log.Warning("Input is not a structured buffer.", this);
            return;
        }

        var hasSlot = _history.TryGetSlot(context, out var history, out var isFirstEvaluation, out var previousSeenFrame);
        if (!hasSlot)
        {
            SetOutputs(src, src);
            return;
        }

        try
        {
            var deviceContext = ResourceManager.Device.ImmediateContext;
            var needsBuffers = history!.A == null || history.B == null
                               || history.A.Buffer == null || history.B.Buffer == null
                               || history.A.Buffer.IsDisposed || history.B.Buffer.IsDisposed
                               || description.SizeInBytes != history.Description.SizeInBytes
                               || description.StructureByteStride != history.Description.StructureByteStride;
            if (needsBuffers)
            {
                history.Dispose();

                // Loops and repeat passes multiply the copies; beyond the budget they get no history. The first
                // evaluation always keeps it, as it did before evaluations were told apart.
                var exceedsBudget = _budget.AllocatedBytes + 2L * description.SizeInBytes > MaxHistoryBytes;
                if (exceedsBudget && !isFirstEvaluation)
                {
                    SetOutputs(src, src);
                    return;
                }

                var newDesc = new BufferDescription
                                  {
                                      SizeInBytes = description.SizeInBytes,
                                      BindFlags = BindFlags.ShaderResource | BindFlags.UnorderedAccess,
                                      CpuAccessFlags = CpuAccessFlags.None,
                                      OptionFlags = ResourceOptionFlags.BufferStructured,
                                      Usage = ResourceUsage.Default,
                                      StructureByteStride = description.StructureByteStride
                                  };
                history.Allocate(newDesc);
            }

            history.Toggle = !history.Toggle;
            deviceContext.CopyResource(src.Buffer, history.Current!.Buffer!);

            // Without last frame's points (new buffers, or the op was bypassed for a while), report no motion instead
            // of reading an uninitialized buffer or streaking from an old state. Paused frames still count as seen.
            var hasPreviousFrame = !needsBuffers && previousSeenFrame == Playback.FrameCount - 1;
            if (!hasPreviousFrame)
                deviceContext.CopyResource(src.Buffer, history.Previous!.Buffer!);
        }
        catch (Exception e)
        {
            Log.Error($"Structured buffer keep failed: {e.Message}", this);
            return;
        }

        SetOutputs(history.Current, history.Previous);
    }

    private void SetOutputs(BufferWithViews? current, BufferWithViews? previous)
    {
        BufferA.Value = current;
        BufferB.Value = previous;
        BufferA.DirtyFlag.Clear();
        BufferB.DirtyFlag.Clear();
    }

    private PointHistory CreatePointHistory()
    {
        return new PointHistory(_budget);
    }

    private static BufferWithViews CreateBuffer(BufferDescription desc)
    {
        var dev = ResourceManager.Device;
        var buf = new Buffer(dev, desc);

        var elementCount = desc.SizeInBytes / Math.Max(1, desc.StructureByteStride);

        var srv = new ShaderResourceView(dev, buf, new ShaderResourceViewDescription
                                                       {
                                                           Format = T3.Graphics.Format.Unknown,
                                                           Dimension = ShaderResourceViewDimension.Buffer,
                                                           Buffer = new ShaderResourceViewDescription.BufferResource
                                                                        {
                                                                            FirstElement = 0,
                                                                            ElementCount = elementCount
                                                                        }
                                                       });

        var uav = new UnorderedAccessView(dev, buf, new UnorderedAccessViewDescription
                                                        {
                                                            Format = T3.Graphics.Format.Unknown,
                                                            Dimension = UnorderedAccessViewDimension.Buffer,
                                                            Buffer = new UnorderedAccessViewDescription.BufferResource
                                                                         {
                                                                             FirstElement = 0,
                                                                             ElementCount = elementCount,
                                                                             Flags = UnorderedAccessViewBufferFlags.None
                                                                         }
                                                        });

        return new BufferWithViews { Buffer = buf, Srv = srv, Uav = uav };
    }

    protected override void Dispose(bool disposing)
    {
        if (!disposing) return;
        _history.Dispose();
    }

    /** Bounds the history of an op inside a long loop; evaluations beyond it report no motion. */
    private const int MaxCallsPerPass = 256;

    /** Point copies of all evaluations together stop here; loops and repeat passes would otherwise multiply them. */
    private const long MaxHistoryBytes = 512L * 1024 * 1024;

    private readonly MemoryBudget _budget = new();
    private readonly EvaluationHistory<PointHistory> _history;

    [Input(Guid = "07ca7e81-02b8-4cb4-92f4-7afaaa07b751")]
    public readonly InputSlot<BufferWithViews> InputBuffer = new();

    [Input(Guid = "513d0146-e0c0-4e27-a3dc-862c1f56e219")]
    public readonly InputSlot<bool> Keep = new();
}
