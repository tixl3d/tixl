namespace Lib.io.video;

/// <summary>
/// Internal helper for [VideoClipPlayer]: iterates the connected video-clip textures and composites each one
/// into the bound render target. Per clip it points the draw subgraph's [UseTextureReference] at that clip's
/// current frame, then re-evaluates the subgraph so [DrawScreenQuad] draws it — the same invalidate-then-evaluate
/// pattern as [Loop], over a texture multi-input instead of a counter. Clips whose TimeClip range doesn't
/// contain the playhead are skipped.
/// </summary>
[Guid("0162ddd9-4611-4a0a-b02f-8f68ded99cfb")]
internal sealed class _ProcessVideoClips : Instance<_ProcessVideoClips>
{
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
        for (var i = 0; i < clips.Count; i++)
        {
            var clip = clips[i];

            // Composite only clips whose timeline range contains the playhead, so a sequential cut-to-cut
            // timeline shows the active clip(s) instead of always the last-drawn one, and gaps composite
            // nothing (the bound target keeps its clear color). A wired texture without a TimeClip (e.g. a
            // plain image) has no range and always draws.
            if (!IsClipActive(clip.Parent, localTime))
                continue;

            var texture = clip.GetValue(context);
            if (texture == null)
                continue;

            // Point the subgraph's [UseTextureReference] at this clip's frame, then re-evaluate the draw
            // subtree so [DrawScreenQuad] composites it into the bound render target.
            reference.ColorTexture = texture;

            DirtyFlag.GlobalInvalidationTick++;
            DrawCommand.InvalidateGraph();
            DrawCommand.GetValue(context);
        }

        Textures.DirtyFlag.Clear();
    }

    // A VideoClip exposes its TimeClip via a TimeClipSlot output (ITimeClipProvider). Exclusive end matches
    // TimeClipSlot's own range test so adjacent clips sharing a cut boundary never both draw on that frame.
    private static bool IsClipActive(Instance clipInstance, double localTime)
    {
        if (clipInstance == null)
            return true;

        var outputs = clipInstance.Outputs;
        for (var i = 0; i < outputs.Count; i++)
        {
            if (outputs[i] is ITimeClipProvider clipProvider)
            {
                var range = clipProvider.TimeClip.TimeRange;
                return localTime >= range.Start && localTime < range.End;
            }
        }

        return true;
    }

    [Input(Guid = "116a67d9-e985-4c2e-a71b-73fbcdadbb18")]
    public readonly MultiInputSlot<Texture2D> Textures = new();

    [Input(Guid = "956080eb-e811-4b4a-bb73-d70a36455fa2")]
    public readonly InputSlot<RenderTargetReference> TextureReference = new();

    [Input(Guid = "b6930a1d-ae4a-4c79-81b4-97ff8c23b681")]
    public readonly InputSlot<Command> DrawCommand = new();
}
