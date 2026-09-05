namespace Lib.render._dx11.api;

[Guid("9d1266c5-23db-439f-a475-8000fdd1c318")]
internal sealed class InputAssemblerStage : Instance<InputAssemblerStage>
{
    [Output(Guid = "18CAE035-C050-4F98-9E5E-B3A6DB70DDA7")]
    public readonly Slot<Command> Output = new(new Command());

    public InputAssemblerStage()
    {
        Output.UpdateAction += Update;
        Output.Value.RestoreAction = Restore;
    }

    private Buffer[] _vertexBuffer = new Buffer[0];
    private void Update(EvaluationContext context)
    {
        var device = ResourceManager.Device;
        var deviceContext = device.ImmediateContext;
        var iaStage = deviceContext.InputAssembler;

        InputLayout.GetValue(context);
        VertexBuffers.GetValues(ref _vertexBuffer, context);
        IndexBuffer.GetValue(context);

        _prevTopology = iaStage.PrimitiveTopology;
        iaStage.PrimitiveTopology = PrimitiveTopology.GetValue(context);
    }

    public void Restore(EvaluationContext context)
    {
        var device = ResourceManager.Device;
        var deviceContext = device.ImmediateContext;
        var iaStage = deviceContext.InputAssembler;

        iaStage.PrimitiveTopology = _prevTopology;
    }

    private PrimitiveTopology _prevTopology;


    [Input(Guid = "1EA95430-B853-4A60-A981-F316905995E8")]
    public readonly InputSlot<PrimitiveTopology> PrimitiveTopology = new();
    [Input(Guid = "B8E07473-60F9-4F5E-995D-7165EF8F7993")]
    public readonly InputSlot<InputLayout> InputLayout = new();
    [Input(Guid = "4A1703D4-5958-4EDE-A755-79A12FE85F3B")]
    public readonly MultiInputSlot<Buffer> VertexBuffers = new();
    [Input(Guid = "C8FD1C4B-E6D6-4CA1-A718-4518E3BFFC59")]
    public readonly InputSlot<Buffer> IndexBuffer = new();
}