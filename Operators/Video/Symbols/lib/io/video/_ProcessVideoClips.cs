#nullable enable annotations

namespace Lib.io.video;

/// <summary>
/// Internal helper for [VideoClipPlayer]: composites the active video clips into the bound render target,
/// lowest LayerIndex on top. Clips come from the wired multi-input and — when AutoCollect is on — from sibling
/// [VideoClip]s in the same composition (deduped). For each it points the draw subgraph's [UseTextureReference]
/// at the clip's frame and re-evaluates the subgraph so [DrawScreenQuad] draws it (the [Loop]
/// invalidate-then-evaluate pattern). Inactive clips are skipped; one about to start is pre-rolled so its first
/// frame is ready at the cut.
/// </summary>
[Guid("0162ddd9-4611-4a0a-b02f-8f68ded99cfb")]
internal sealed class _ProcessVideoClips : Instance<_ProcessVideoClips>
{
    private enum ClipState
    {
        Inactive,
        Active,
        Upcoming,
    }

    private readonly struct ClipEntry(Slot<Texture2D> textureSlot, IVideoClipProvider provider, T3.Core.Animation.TimeClip timeClip)
    {
        public readonly Slot<Texture2D> TextureSlot = textureSlot;
        public readonly IVideoClipProvider Provider = provider; // null for a plain wired texture (no per-clip params)
        public readonly T3.Core.Animation.TimeClip TimeClip = timeClip; // null for a plain wired texture
        public readonly int LayerIndex = timeClip?.LayerIndex ?? 0;
    }

    [Output(Guid = "4022374f-2022-466d-9787-a7c47fe45737")]
    public readonly Slot<Command> Output = new();

    public _ProcessVideoClips()
    {
        Output.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var reference = TextureReference.GetValue(context);
        if (reference == null)
            return;

        var localTime = context.LocalTime;

        _activeEntries.Clear();
        _seenChildIds.Clear();

        // Wired clips (the multi-input). Active ones are insertion-sorted by layer; one about to start is warmed
        // (preroll). Every wired clip's id is recorded so the AutoCollect scan below can't add it twice.
        var clips = Textures.CollectedInputs;
        for (var i = 0; i < clips.Count; i++)
        {
            var slot = clips[i];
            var instance = slot.Parent;
            if (instance != null)
                _seenChildIds.Add(instance.SymbolChildId);

            // Follow the wired texture chain upstream to the originating clip op, so image effects inserted
            // between a [VideoClip] and this player keep the clip's time-gating and per-clip params — the
            // drawn texture stays the wired (post-effect) one.
            var clipSource = FindUpstreamClipSource(instance);
            if (clipSource != null)
                _seenChildIds.Add(clipSource.SymbolChildId); // the underlying clip must not auto-collect a second time

            var provider = clipSource as IVideoClipProvider;
            provider?.MarkManaged(); // mark before classifying, so an inactive-but-managed clip doesn't hint

            var state = ClassifyClip(clipSource, localTime, out var timeClip);
            if (state == ClipState.Upcoming)
                slot.GetValue(context);
            else if (state == ClipState.Active)
                InsertActive(new ClipEntry(slot, provider, timeClip));
        }

        // Auto-collected sibling [VideoClip]s in the same composition (unwired), if enabled. Force-evaluating
        // them works because VideoClip.Texture is DirtyFlagTrigger.Animated (always re-evaluates), so a plain
        // GetValue re-decodes for the current time without an explicit InvalidateGraph. Deduped against the
        // wired set (and each other) by SymbolChildId so a clip that's both wired and a sibling draws once.
        if (AutoCollect.GetValue(context) && Parent?.Parent is { } composition)
        {
            // Rebuild the sibling-clip list only when the composition instance changes (focus / hot-reload)
            // or its Symbol.VersionCounter bumps (any edit) — not every frame. Avoids the per-frame
            // Children.Values walk + locked per-child lookup, a frame-drop source on large graphs.
            if (!ReferenceEquals(_cachedComposition, composition) || _cachedStructureVersion != composition.Symbol.VersionCounter)
            {
                _cachedComposition = composition;
                _cachedStructureVersion = composition.Symbol.VersionCounter;
                _autoCollectedChildren.Clear();
                foreach (var child in composition.Children.Values)
                {
                    if (child is IVideoClipProvider)
                        _autoCollectedChildren.Add(child);
                }
            }

            for (var i = 0; i < _autoCollectedChildren.Count; i++)
            {
                var child = _autoCollectedChildren[i];
                if (!_seenChildIds.Add(child.SymbolChildId))
                    continue;
                if (child is not IVideoClipProvider provider)
                    continue;

                provider.MarkManaged();

                var state = ClassifyClip(child, localTime, out var timeClip);
                if (state == ClipState.Upcoming)
                    provider.TextureOutput.GetValue(context);
                else if (state == ClipState.Active)
                    InsertActive(new ClipEntry(provider.TextureOutput, provider, timeClip));
            }
        }

        // Composite the active set into the bound target, lowest LayerIndex on top (the list is descending by
        // layer). Per-clip color (tint + alpha opacity) rides context.ForegroundColor (restored after the loop);
        // the wrapper feeds it into [DrawScreenQuad].Color via [GetForegroundColor]. Blend mode goes through a
        // context variable read by [GetIntVar].
        var baseForeground = context.ForegroundColor;
        for (var a = 0; a < _activeEntries.Count; a++)
        {
            var entry = _activeEntries[a];
            var texture = entry.TextureSlot.GetValue(context);
            if (texture == null)
                continue;

            // A wired texture that isn't a [VideoClip] (no provider) keeps the white / Normal defaults.
            // Per-clip params are the clip's *inputs*, pulled here with the player's context — so animated
            // values (e.g. a Color fade) must be sampled in the clip's local time, like everything the clip
            // evaluates itself. Remap around the pulls, mirroring TimeClipSlot.
            var color = Vector4.One;
            var blendMode = 0;
            if (entry.Provider != null)
            {
                var prevTime = context.LocalTime;
                var prevFxTime = context.LocalFxTime;
                if (entry.TimeClip != null)
                {
                    context.LocalTime = entry.TimeClip.MapTimelineToSource(prevTime);
                    context.LocalFxTime = entry.TimeClip.MapTimelineToSource(prevFxTime);
                }

                color = entry.Provider.ColorInput.GetValue(context);
                blendMode = entry.Provider.BlendModeInput.GetValue(context);

                context.LocalTime = prevTime;
                context.LocalFxTime = prevFxTime;
            }

            context.ForegroundColor = baseForeground * color;
            context.IntVariables[BlendModeVariableName] = blendMode;

            // Point the subgraph's [UseTextureReference] at this clip's frame, then re-evaluate the draw subtree
            // so [DrawScreenQuad] composites it into the bound render target.
            reference.ColorTexture = texture;

            DirtyFlag.GlobalInvalidationTick++;
            DrawCommand.InvalidateGraph();
            DrawCommand.GetValue(context);
        }

        context.ForegroundColor = baseForeground;
        Textures.DirtyFlag.Clear();
    }

    // Keep _activeEntries descending by LayerIndex so the draw loop paints the highest layer first (back) and
    // the lowest last (on top). Stable for equal layers — stops at the first entry with a strictly lower layer,
    // so ties keep insertion order (wired before auto-collected, connection order within each).
    private void InsertActive(ClipEntry entry)
    {
        var pos = _activeEntries.Count;
        while (pos > 0 && _activeEntries[pos - 1].LayerIndex < entry.LayerIndex)
            pos--;
        _activeEntries.Insert(pos, entry);
    }

    /// <summary>
    /// Walks the wired texture chain upstream (first connected texture input each hop, depth-limited) and
    /// returns the first instance that is a clip — has an <see cref="ITimeClipProvider"/> output or implements
    /// <see cref="IVideoClipProvider"/>. Returns the wired instance itself when it is already a clip, or null
    /// when the chain holds no clip (e.g. a plain image source).
    /// </summary>
    private static Instance? FindUpstreamClipSource(Instance? instance)
    {
        for (var depth = 0; instance != null && depth < MaxUpstreamSearchDepth; depth++)
        {
            if (instance is IVideoClipProvider || HasTimeClipOutput(instance))
                return instance;

            Instance? next = null;
            var inputs = instance.Inputs;
            for (var i = 0; i < inputs.Count; i++)
            {
                if (inputs[i] is not InputSlot<Texture2D> textureInput)
                    continue;

                if (textureInput.TryGetFirstConnection(out var source))
                {
                    next = source.Parent;
                    break;
                }
            }

            instance = next;
        }

        return null;
    }

    private static bool HasTimeClipOutput(Instance instance)
    {
        var outputs = instance.Outputs;
        for (var i = 0; i < outputs.Count; i++)
        {
            if (outputs[i] is ITimeClipProvider)
                return true;
        }

        return false;
    }

    private const int MaxUpstreamSearchDepth = 8;

    // A VideoClip exposes its TimeClip via a TimeClipSlot output (ITimeClipProvider). Classifies the clip at
    // localTime and (out) its TimeClip, which the caller needs for layer ordering and for remapping the
    // per-clip param pulls into clip-local time. Exclusive end matches TimeClipSlot's own range test so
    // adjacent clips sharing a cut boundary never both draw on that frame. Upcoming = within PrerollSeconds
    // before the clip's start, so its decoder can be warmed ahead of the cut. A wired texture without a
    // TimeClip (e.g. a plain image) has no range, so it is always active with a null clip (layer 0).
    private static ClipState ClassifyClip(Instance clipInstance, double localTime, out T3.Core.Animation.TimeClip timeClip)
    {
        timeClip = null;
        if (clipInstance == null)
            return ClipState.Active;

        var outputs = clipInstance.Outputs;
        for (var i = 0; i < outputs.Count; i++)
        {
            if (outputs[i] is ITimeClipProvider clipProvider)
            {
                // A disabled clip reads as "not there" — like outside its range — instead of
                // compositing its frozen last frame.
                if (clipProvider is Slot<Texture2D> { IsDisabled: true })
                    return ClipState.Inactive;

                timeClip = clipProvider.TimeClip;
                var range = timeClip.TimeRange;
                if (localTime >= range.Start && localTime < range.End)
                    return ClipState.Active;
                if (localTime >= range.Start - PrerollSeconds && localTime < range.Start)
                    return ClipState.Upcoming;
                return ClipState.Inactive;
            }
        }

        return ClipState.Active;
    }

    // Variable name the wrapper's draw subgraph reads via [GetIntVar] to pick up the active clip's blend mode.
    // Must match exactly what that op is configured with in [VideoClipPlayer]. (Opacity rides ForegroundColor.)
    private const string BlendModeVariableName = "VideoClip.BlendMode";

    // Forward look-ahead for warming a clip's decoder before its cut-in (covers cold open + seek + first-GOP
    // decode). Timeline seconds; very fast playback leaves less wall-clock to preroll, so it may still blink.
    // Direction-aware preroll for reverse play lives in the engine scheduler (later).
    private const double PrerollSeconds = 0.5;

    // Reused across frames (Update runs on the single graph-eval thread); cleared, not reallocated, each frame.
    private readonly List<ClipEntry> _activeEntries = new();
    private readonly HashSet<Guid> _seenChildIds = new();

    // AutoCollect scan cache — rebuilt only when the composition instance changes (reload) or its
    // Symbol.VersionCounter bumps (any edit), not every frame.
    private Instance? _cachedComposition;
    private int _cachedStructureVersion = -1;
    private readonly List<Instance> _autoCollectedChildren = new();

    [Input(Guid = "116a67d9-e985-4c2e-a71b-73fbcdadbb18")]
    public readonly MultiInputSlot<Texture2D> Textures = new();

    [Input(Guid = "956080eb-e811-4b4a-bb73-d70a36455fa2")]
    public readonly InputSlot<RenderTargetReference> TextureReference = new();

    [Input(Guid = "b6930a1d-ae4a-4c79-81b4-97ff8c23b681")]
    public readonly InputSlot<Command> DrawCommand = new();

    [Input(Guid = "a431cf04-491d-4b0d-9691-a0375e07e855")]
    public readonly InputSlot<bool> AutoCollect = new();
}
