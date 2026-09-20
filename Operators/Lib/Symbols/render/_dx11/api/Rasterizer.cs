namespace Lib.render._dx11.api;

[Guid("fbd7f0f0-36a3-4fbb-91e1-cb33d4666d09")]
internal sealed class Rasterizer : Instance<Rasterizer>
{
    [Output(Guid = "C723AD69-FF0C-47B2-9327-BD27C0D7B6D1")]
    public readonly Slot<Command> Output = new(new Command());

    public Rasterizer()
    {
        Output.UpdateAction += Update;
        Output.Value.RestoreAction = Restore;
    }

    private void Update(EvaluationContext context)
    {
        var device = ResourceManager.Device;
        var deviceContext = device.ImmediateContext;
        var rasterizer = deviceContext.Rasterizer;

        ScissorRectangles.GetValue(context);

        // Saved explicitly, and restored by the enclosing operator.
        deviceContext.PushState(StateGroups.Rasterizer);

        Viewports.GetValues(ref _viewports, context);

        var newState = RasterizerState.GetValue(context);
        rasterizer.State = newState;
        
        if (_viewports.Length > 0)
            rasterizer.SetViewports(_viewports, _viewports.Length);
    }

    private void Restore(EvaluationContext context)
    {
        ResourceManager.Device.ImmediateContext.PopState();
    }

    private T3.Graphics.Viewport[] _viewports = [];

    [Input(Guid = "35A52074-1E82-4352-91C3-D8E464F73BC7")]
    public readonly InputSlot<RasterizerState> RasterizerState = new();
    [Input(Guid = "73945E5D-3C3C-4742-B341-A061B0DC116F")]
    public readonly MultiInputSlot<T3.Graphics.Viewport> Viewports = new();
    [Input(Guid = "3F71BE22-9DC2-4E47-8B3A-1EF3C9ECBD9D")]
    public readonly MultiInputSlot<ScissorRect> ScissorRectangles = new();

}