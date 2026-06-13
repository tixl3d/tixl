#nullable enable
using T3.Core.DataTypes;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.Interaction.Variations.Model;
using T3.Editor.Gui.UiHelpers.Thumbnails;
using T3.Editor.Gui.Windows.RenderExport;

namespace T3.Editor.Gui.Interaction.Variations;

/// <summary>
/// Renders variation thumbnails independent of the Variations window. Requests are queued and
/// processed one per frame from <see cref="VariationHandling.Update"/>, using the pinned output
/// (<see cref="RenderProcess"/>) as the render target. This lets callers like the snapshot control
/// view capture a thumbnail when the Variations window isn't open.
/// </summary>
/// <remarks>
/// The Variations canvas still owns its live-preview / "Update thumbnails" loop. To avoid both
/// driving the same pinned output in the same frames, the canvas calls <see cref="NotifyCanvasRendered"/>
/// while it renders, and this service yields for a few frames afterwards.
/// </remarks>
internal static class VariationThumbnailRenderer
{
    /// <summary>
    /// Queues a thumbnail render for one variation. <paramref name="toFile"/> saves the curated
    /// default (PackageMeta); otherwise it goes to the live/temp cache.
    /// </summary>
    internal static void RequestRender(SymbolVariationPool pool, Instance instance, Guid variationId, bool toFile)
    {
        if (pool == null! || instance == null!)
            return;

        _queue.Enqueue(new Request(pool, instance, variationId, toFile));
    }

    /// <summary>Called by the Variations canvas while it renders, so this service stands aside.</summary>
    internal static void NotifyCanvasRendered() => _yieldFrames = CanvasYieldFrames;

    internal static void Update()
    {
        if (_yieldFrames > 0)
            _yieldFrames--;

        if (_pending == null && _queue.Count == 0)
            return;

        if (!TryGetOutputSlot(out var outputSlot))
        {
            // No render target (no output window yet). Don't let requests pile up forever.
            if (++_framesWithoutOutput > MaxFramesWithoutOutput)
            {
                _queue.Clear();
                _pending = null;
            }

            return;
        }

        _framesWithoutOutput = 0;

        // Finish an in-flight capture first — the variation was applied last frame so render and
        // readback don't race.
        if (_pending != null)
        {
            if (_captureDelayFrames > 0)
            {
                _captureDelayFrames--;
                return;
            }

            Capture(outputSlot, _pending);
            _pending = null;
            return;
        }

        // Don't start while the canvas is (or just was) driving the same output.
        if (_yieldFrames > 0)
            return;

        var request = _queue.Dequeue();
        if (request.Instance == null! || request.Pool == null!)
            return;

        if (!TryGetVariation(request, out var variation))
            return;

        request.Pool.BeginHover(request.Instance, variation);
        outputSlot.DirtyFlag.ForceInvalidate();
        outputSlot.Update(_context);

        _pending = request;
        _captureDelayFrames = CaptureDelayFrames;
    }

    private static void Capture(Slot<Texture2D> outputSlot, Request request)
    {
        outputSlot.DirtyFlag.ForceInvalidate();
        outputSlot.Update(_context);

        var category = request.ToFile ? ThumbnailManager.Categories.PackageMeta : ThumbnailManager.Categories.Temp;
        // Cover (crop) rather than letterbox — the 16:9 output otherwise leaves bars in the 4:3 slot.
        ThumbnailManager.SaveThumbnail(request.VariationId, request.Instance.Symbol.SymbolPackage, outputSlot.Value, category,
                                       saveToFile: request.ToFile, cover: true);

        request.Pool.StopHover();
    }

    private static bool TryGetOutputSlot(out Slot<Texture2D> outputSlot)
    {
        outputSlot = null!;
        if (RenderProcess.OutputWindow == null || RenderProcess.State != RenderProcess.States.ReadyForExport)
            return false;

        var shownInstance = RenderProcess.OutputWindow.ShownInstance;
        if (shownInstance is not { Outputs.Count: > 0 })
            return false;

        if (shownInstance.Outputs[0] is not Slot<Texture2D> textureSlot)
            return false;

        outputSlot = textureSlot;
        return true;
    }

    private static bool TryGetVariation(Request request, out Variation variation)
    {
        variation = null!;
        foreach (var v in request.Pool.AllVariations)
        {
            if (v.Id != request.VariationId)
                continue;

            variation = v;
            return true;
        }

        return false;
    }

    private sealed record Request(SymbolVariationPool Pool, Instance Instance, Guid VariationId, bool ToFile);

    private const int CaptureDelayFrames = 1;
    private const int CanvasYieldFrames = 3;
    private const int MaxFramesWithoutOutput = 600;

    private static readonly EvaluationContext _context = new() { RequestedResolution = new Int2(170, 130) };
    private static readonly Queue<Request> _queue = new();
    private static Request? _pending;
    private static int _captureDelayFrames;
    private static int _yieldFrames;
    private static int _framesWithoutOutput;
}
