namespace Lib.io.video;

/// <summary>
/// Internal helper for [VideoClipPlayer]: iterates the connected video-clip textures and composites each one
/// into the bound render target. Per clip it points the draw subgraph's [UseTextureReference] at that clip's
/// current frame, then re-evaluates the subgraph so [DrawScreenQuad] draws it — the same invalidate-then-evaluate
/// pattern as [Loop], over a texture multi-input instead of a counter. Clips whose TimeClip range doesn't
/// contain the playhead are skipped; one about to start is pre-rolled so its first frame is ready at the cut.
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
        var clips = Textures.CollectedInputs;
        var count = clips.Count;

        if (_activeIndices.Length < count)
        {
            _activeIndices = new int[count];
            _activeLayers = new int[count];
        }

        // Gather the clips whose timeline range contains the playhead (a sequential cut-to-cut timeline then
        // shows only the active clip(s); gaps composite nothing and the bound target keeps its clear color),
        // insertion-sorting each into descending LayerIndex order as it's found. The active set is tiny
        // (usually 1-3); the lowest layer ends up last so it draws on top, and equal layers keep multi-input
        // connection order (stable: `i` only advances and the shift condition is strict ( < )).
        var activeCount = 0;
        for (var i = 0; i < count; i++)
        {
            var state = ClassifyClip(clips[i].Parent, localTime, out var layerIndex);

            if (state == ClipState.Upcoming)
            {
                // Preroll: pulling the texture warms the clip's decoder (open + seek to in-point + decode the
                // first GOP) without compositing it, so the frame is ready at the cut instead of a transparent
                // blink. The engine keeps the warmed stream live until the clip turns active.
                clips[i].GetValue(context);
                continue;
            }

            if (state != ClipState.Active)
                continue;

            var b = activeCount - 1;
            while (b >= 0 && _activeLayers[b] < layerIndex)
            {
                _activeIndices[b + 1] = _activeIndices[b];
                _activeLayers[b + 1] = _activeLayers[b];
                b--;
            }
            _activeIndices[b + 1] = i;
            _activeLayers[b + 1] = layerIndex;
            activeCount++;
        }

        // Per-clip color (tint + alpha opacity) rides context.ForegroundColor (restored after the loop); the
        // wrapper feeds it into [DrawScreenQuad].Color via [GetForegroundColor]. Blend mode goes through a
        // context variable read by [GetIntVar].
        var baseForeground = context.ForegroundColor;
        for (var a = 0; a < activeCount; a++)
        {
            var clipSlot = clips[_activeIndices[a]];
            var texture = clipSlot.GetValue(context);
            if (texture == null)
                continue;

            // A wired texture that isn't a [VideoClip] keeps the white / Normal defaults.
            var color = Vector4.One;
            var blendMode = 0;
            if (clipSlot.Parent is IVideoClipProvider provider)
            {
                color = provider.ColorInput.GetValue(context);
                blendMode = provider.BlendModeInput.GetValue(context);
            }

            context.ForegroundColor = baseForeground * color;
            context.IntVariables[BlendModeVariableName] = blendMode;

            // Point the subgraph's [UseTextureReference] at this clip's frame, then re-evaluate the draw
            // subtree so [DrawScreenQuad] composites it into the bound render target.
            reference.ColorTexture = texture;

            DirtyFlag.GlobalInvalidationTick++;
            DrawCommand.InvalidateGraph();
            DrawCommand.GetValue(context);
        }

        context.ForegroundColor = baseForeground;
        Textures.DirtyFlag.Clear();
    }

    // A VideoClip exposes its TimeClip via a TimeClipSlot output (ITimeClipProvider). Classifies the clip at
    // localTime and (out) its layer index. Exclusive end matches TimeClipSlot's own range test so adjacent
    // clips sharing a cut boundary never both draw on that frame. Upcoming = within PrerollSeconds before the
    // clip's start, so its decoder can be warmed ahead of the cut. A wired texture without a TimeClip (e.g. a
    // plain image) has no range, so it is always active and sits on layer 0.
    private static ClipState ClassifyClip(Instance clipInstance, double localTime, out int layerIndex)
    {
        layerIndex = 0;
        if (clipInstance == null)
            return ClipState.Active;

        var outputs = clipInstance.Outputs;
        for (var i = 0; i < outputs.Count; i++)
        {
            if (outputs[i] is ITimeClipProvider clipProvider)
            {
                var clip = clipProvider.TimeClip;
                layerIndex = clip.LayerIndex;
                var range = clip.TimeRange;
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

    // Reused across frames; grown to the connected-clip count on first use (and if more clips are wired in).
    private int[] _activeIndices = [];
    private int[] _activeLayers = [];

    [Input(Guid = "116a67d9-e985-4c2e-a71b-73fbcdadbb18")]
    public readonly MultiInputSlot<Texture2D> Textures = new();

    [Input(Guid = "956080eb-e811-4b4a-bb73-d70a36455fa2")]
    public readonly InputSlot<RenderTargetReference> TextureReference = new();

    [Input(Guid = "b6930a1d-ae4a-4c79-81b4-97ff8c23b681")]
    public readonly InputSlot<Command> DrawCommand = new();
}
