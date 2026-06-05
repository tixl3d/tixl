namespace Lib.io.video;

/// <summary>
/// Internal helper for [VideoClipPlayer]: iterates the connected video-clip textures and composites each one
/// into the bound render target. Per clip it points the draw subgraph's [UseTextureReference] at that clip's
/// current frame, then re-evaluates the subgraph so [DrawQuad] draws it — the same invalidate-then-evaluate
/// pattern as [Loop], over a texture multi-input instead of a counter.
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

        foreach (var clip in Textures.CollectedInputs)
        {
            var texture = clip.GetValue(context);
            if (texture == null)
                continue;

            // Point the subgraph's [UseTextureReference] at this clip's frame, then re-evaluate the draw
            // subtree so [DrawQuad] composites it into the bound render target.
            reference.ColorTexture = texture;

            DirtyFlag.GlobalInvalidationTick++;
            DrawCommand.InvalidateGraph();
            DrawCommand.GetValue(context);
        }

        Textures.DirtyFlag.Clear();
    }

    [Input(Guid = "116a67d9-e985-4c2e-a71b-73fbcdadbb18")]
    public readonly MultiInputSlot<Texture2D> Textures = new();

    [Input(Guid = "956080eb-e811-4b4a-bb73-d70a36455fa2")]
    public readonly InputSlot<RenderTargetReference> TextureReference = new();

    [Input(Guid = "b6930a1d-ae4a-4c79-81b4-97ff8c23b681")]
    public readonly InputSlot<Command> DrawCommand = new();
}
