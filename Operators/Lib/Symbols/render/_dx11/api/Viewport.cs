namespace Lib.render._dx11.api;

[Guid("1f23db4a-871e-42a9-9255-49b956993eb1")]
internal sealed class Viewport : Instance<Viewport>
{
    [Output(Guid = "C543AF89-018E-4540-9F65-32CF6688CD42")]
    public readonly Slot<RawViewportF> Output = new();

    public Viewport()
    {
        Output.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        Output.Value = new RawViewportF
                           {
                               X = X.GetValue(context),
                               Y = Y.GetValue(context),
                               Width = Width.GetValue(context),
                               Height = Height.GetValue(context),
                               MinDepth = MinDepth.GetValue(context),
                               MaxDepth = MaxDepth.GetValue(context)
                           };
    }

    [Input(Guid = "65647489-4AD9-4D8C-8B4F-EEB726846488")]
    public readonly InputSlot<float> X = new();
    [Input(Guid = "33DA799A-EFF2-4E0A-9F8B-7F65CA03A350")]
    public readonly InputSlot<float> Y = new();
    [Input(Guid = "7A4DBBAC-B863-49D9-AD42-3F218683BCB1")]
    public readonly InputSlot<float> Width = new();
    [Input(Guid = "ACC2B98A-ED9F-4B7A-A274-480AF6F50335")]
    public readonly InputSlot<float> Height = new();
    [Input(Guid = "E378460F-44D2-4D73-97DE-34CCFACB11A3")]
    public readonly InputSlot<float> MinDepth = new();
    [Input(Guid = "4F926315-5826-42CD-B35F-48DB63E8D20E")]
    public readonly InputSlot<float> MaxDepth = new();
}