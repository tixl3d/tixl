#nullable enable
using System;
using System.Collections.Generic;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using Texture2D = T3.Core.DataTypes.Texture2D;
using Int2 = T3.Core.DataTypes.Vector.Int2;

namespace T3.Core.Output.Rendering;

/// <summary>
/// Resolves setup routing (slice → source → send op) to live textures and pulls that content through one
/// shared evaluation context. Content is pulled once per send op per frame, so several surfaces slicing
/// one image cost a single upstream evaluation, and the surface→slice chain of finds is memoised per frame.
/// </summary>
public static class OutputContentResolver
{
    /// <summary>The shared evaluation context, valid after <see cref="PrepareContext"/> ran this frame.</summary>
    public static EvaluationContext Context => _context ??= new EvaluationContext();

    /// <summary>
    /// Readies the shared evaluation context for pulling content this frame: fresh state, the resolution the
    /// content is asked to render at, and the once-per-frame invalidation of every send — whichever entry
    /// point pulls first (a composite, or a source preview on the Board) does it.
    /// </summary>
    public static EvaluationContext PrepareContext(Int2 requestedResolution)
    {
        var context = Context;
        context.Reset();
        context.RequestedResolution = requestedResolution;
        InvalidateContentOncePerFrame(context);
        return context;
    }

    /// <summary>Pulls a send's content at the context's requested resolution. Views showing any op along the way
    /// see its slot as updated this frame and draw it without evaluating again.</summary>
    public static Texture2D? PullContent(IContentSupplier supplier)
    {
        var context = Context;
        var frame = OutputFrame.Index;
        if (frame != _pulledContentFrame)
        {
            _pulledContentFrame = frame;
            _pulledContent.Clear();
        }

        // What this send was asked for, so it can warn when two outputs disagree about its size.
        if (supplier is Instance instance)
            OutputContentStats.NotePull(instance.SymbolChildId, context.RequestedResolution, frame);

        // One evaluation per send and frame, however many patches and surfaces cut from it: a graph with an
        // always-dirty op renders again on every pull, and a second render lands in the same texture anyway,
        // so every consumer would show the last one regardless.
        if (_pulledContent.TryGetValue(supplier, out var pulled))
            return pulled;

        var content = supplier.GetContent(context);
        _pulledContent[supplier] = content;
        if (supplier is Instance pulledInstance)
            _lastContentByChildId[pulledInstance.SymbolChildId] = content;

        return content;
    }

    /// <summary>
    /// A content source's texture for a preview (a Board card, a parameter thumbnail): evaluated when the
    /// preview budget allows, otherwise the last texture it produced. See <see cref="OutputPreviewRefresh"/>.
    /// </summary>
    public static bool TryGetPreviewContent(Guid symbolChildId, out Texture2D? content)
    {
        if (!OutputPreviewRefresh.ShouldRefresh(symbolChildId))
        {
            content = _lastContentByChildId.GetValueOrDefault(symbolChildId);
            return content is { IsDisposed: false };
        }

        return TryGetSourceContent(symbolChildId, out _, out content);
    }

    /// <summary>The live texture a content source resolves to, if its op is currently instantiated.</summary>
    public static bool TryGetSourceContent(Guid symbolChildId, out IContentSupplier? supplier, out Texture2D? content)
    {
        content = null;
        supplier = FindSendByChildId(symbolChildId);
        var setup = ActiveSetup.Current;
        if (supplier == null || setup == null)
            return false;

        PrepareContext(RequestedResolutionFor(setup, symbolChildId));
        content = PullContent(supplier);
        return content is { IsDisposed: false };
    }

    /// <summary>
    /// The size a send's content is asked to render at when nothing composites it this frame: the canvas of
    /// the output it is routed to, else the first output with a size, so a preview matches what a binding
    /// would show and never re-renders the graph at a size of its own.
    /// </summary>
    public static Int2 RequestedResolutionFor(Setup setup, Guid symbolChildId)
    {
        if (setup.TryGetOutputOfSend(symbolChildId, out var outputId))
        {
            var routed = setup.FindOutput(outputId);
            if (routed != null && TryGetFittedPatchRequest(setup, routed, symbolChildId, out var fitted))
                return fitted;

            if (routed != null && routed.ResolvedResolution.Width > 0 && routed.ResolvedResolution.Height > 0)
                return routed.ResolvedResolution;
        }

        for (var i = 0; i < setup.Outputs.Count; i++)
        {
            var r = setup.Outputs[i].ResolvedResolution;
            if (r.Width > 0 && r.Height > 0)
                return r;
        }

        return new Int2(1920, 1080);
    }

    /// <summary>
    /// The size a fitted patch asks its content to render at: the patch's own pixel size along the picture,
    /// divided by the share of the source its slice cuts, so the full content has exactly the patch's aspect and
    /// the slice lands 1:1. That is what makes a fit a fit rather than the canvas-sized content squeezed into it.
    /// False for a stretched patch, which keeps asking for the canvas size.
    /// </summary>
    public static bool TryGetFittedRequest(OutputDefinition output, OutputDefinition.Patch patch, Vector4 uvRect, out Int2 resolution)
    {
        resolution = default;
        if (!patch.IsFitted || patch.Quad.Length < 4)
            return false;

        var canvas = output.CanvasSize;
        var width = (patch.Quad[1].X - patch.Quad[0].X) * canvas.X;
        var height = (patch.Quad[3].Y - patch.Quad[0].Y) * canvas.Y;
        if ((OutputDefinition.Patch.NormalizeTurns(patch.QuarterTurns) & 1) == 1)
            (width, height) = (height, width);

        var uvWidth = MathF.Max(uvRect.Z - uvRect.X, 0.0001f);
        var uvHeight = MathF.Max(uvRect.W - uvRect.Y, 0.0001f);
        resolution = new Int2(Math.Clamp((int)MathF.Round(width / uvWidth), 1, MaxRequestedSize),
                              Math.Clamp((int)MathF.Round(height / uvHeight), 1, MaxRequestedSize));
        return true;
    }

    /// <summary>
    /// What a surface shows, resolved through the setup: its slice, the source that slice cuts from, and the
    /// live texture behind it.
    /// </summary>
    public static bool TryGetSurfaceSlice(Guid surfaceId, out Slice? slice, out Texture2D? content, out Vector4 uv)
    {
        // Every card, region and traced quad asks per frame; the chain of linear finds behind it is answered once.
        var frame = OutputFrame.Index;
        if (frame != _surfaceSliceFrame)
        {
            _surfaceSliceFrame = frame;
            _surfaceSlices.Clear();
        }

        if (!_surfaceSlices.TryGetValue(surfaceId, out var resolved))
        {
            resolved = ResolveSurfaceSlice(surfaceId);
            _surfaceSlices[surfaceId] = resolved;
        }

        slice = resolved.Slice;
        content = resolved.Content;
        // The UV is read live: a crop or pan edits it mid-frame and the previews must follow within the frame.
        uv = resolved.Slice?.UvRect ?? _fullUvRect;
        return resolved.Found;
    }

    /// <summary>
    /// A slice's live send op and its uv rect: <c>Slice → SourceId → ContentSource → SymbolChildId → op</c>.
    /// Routing is setup data, so it survives the op being re-instantiated.
    /// </summary>
    public static bool TryResolveSliceContent(Setup setup, Guid sliceId, out IContentSupplier? supplier, out Vector4 sourceRect)
    {
        supplier = null;
        sourceRect = _fullUvRect;
        if (sliceId == Guid.Empty)
            return false;

        var slice = setup.FindSlice(sliceId);
        var source = slice == null ? null : setup.FindSource(slice.SourceId);
        if (source == null)
            return false;

        supplier = FindSendByChildId(source.SymbolChildId);
        sourceRect = slice!.UvRect;
        return supplier != null;
    }

    public static bool TryResolveSurfaceContent(Setup setup, Surface surface, out IContentSupplier? supplier, out Vector4 sourceRect)
    {
        return TryResolveSliceContent(setup, surface.SliceId, out supplier, out sourceRect);
    }

    /// <summary>Drops the surface→slice memo; the next frame re-resolves against the new setup.</summary>
    public static void ReleaseAll()
    {
        _surfaceSlices.Clear();
    }

    private static void InvalidateContentOncePerFrame(EvaluationContext context)
    {
        var frame = OutputFrame.Index;
        if (frame == _invalidatedContentFrame)
            return;

        _invalidatedContentFrame = frame;

        DirtyFlag.GlobalInvalidationTick++;
        foreach (var supplier in ContentSupplierRegistry.Suppliers)
        {
            // Update=false freezes this content at its last frame — skip its invalidation.
            if (supplier.GetUpdateEnabled(context))
                supplier.InvalidateContent();
        }
    }

    private static (bool Found, Slice? Slice, Texture2D? Content) ResolveSurfaceSlice(Guid surfaceId)
    {
        var setup = ActiveSetup.Current;
        var surface = setup?.FindSurface(surfaceId);
        if (setup == null || surface == null || surface.SliceId == Guid.Empty)
            return (false, null, null);

        var slice = setup.FindSlice(surface.SliceId);
        var sourceId = slice?.SourceId ?? Guid.Empty;
        var source = sourceId == Guid.Empty ? null : setup.FindSource(sourceId);
        if (source == null || !TryGetPreviewContent(source.SymbolChildId, out var content))
            return (false, slice, null);

        return (true, slice, content);
    }

    private static IContentSupplier? FindSendByChildId(Guid childId)
    {
        foreach (var supplier in ContentSupplierRegistry.Suppliers)
        {
            if (supplier is Instance instance && instance.SymbolChildId == childId)
                return supplier;
        }

        return null;
    }

    private static readonly Vector4 _fullUvRect = new(0, 0, 1, 1);
    private static EvaluationContext? _context;
    private static int _invalidatedContentFrame = -1;
    private static readonly Dictionary<Guid, (bool Found, Slice? Slice, Texture2D? Content)> _surfaceSlices = new();
    private static int _surfaceSliceFrame = -1;
    private static readonly Dictionary<IContentSupplier, Texture2D?> _pulledContent = new();

    /** Kept across frames so a preview that isn't evaluating this frame still has something to show. */
    private static readonly Dictionary<Guid, Texture2D?> _lastContentByChildId = new();
    private static int _pulledContentFrame = -1;

    /// <summary>The first fitted patch on <paramref name="output"/> that shows this send's content, as a request.</summary>
    private static bool TryGetFittedPatchRequest(Setup setup, OutputDefinition output, Guid symbolChildId, out Int2 resolution)
    {
        resolution = default;
        var source = setup.FindSourceByChildId(symbolChildId);
        if (source == null)
            return false;

        foreach (var patch in output.Patches)
        {
            if (!patch.IsFitted)
                continue;

            var slice = setup.FindSlice(patch.SliceId);
            if (slice != null && slice.SourceId == source.Id && TryGetFittedRequest(output, patch, slice.UvRect, out resolution))
                return true;
        }

        return false;
    }

    private const int MaxRequestedSize = 16384;
}
