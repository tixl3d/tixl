namespace Lib.point.draw;

[Guid("44d57201-addf-455c-9e31-709b018ba29f")]
internal sealed class DrawConnectionLines : Instance<DrawConnectionLines>
{

    [Output(Guid = "5f0e1d16-41a4-4c55-95c3-6e3b66a724b7")]
    public readonly Slot<Command> Output = new();

    [Input(Guid = "1bf7d8c0-1c8c-4791-8960-389869059489")]
    public readonly InputSlot<BufferWithViews> PointsA_ = new();

    [Input(Guid = "db5ec31c-ea35-4780-a054-67ce9fbe80ec")]
    public readonly InputSlot<float> CellSize = new();

    [Input(Guid = "34fe157f-dfe8-4cc0-aa66-6cda433155fb")]
    public readonly InputSlot<int> TestIndex = new();

    [Input(Guid = "e84dcbfa-04e4-4acd-8fcd-b1da20abd71f")]
    public readonly InputSlot<bool> IsEnabled = new();

    [Input(Guid = "7ae1d0c1-b9a7-47f1-8a0c-80bfc1219c4b")]
    public readonly InputSlot<int> StepCount = new();

    [Input(Guid = "496e1983-f74e-412c-9b80-8dedd0f8c1f8")]
    public readonly InputSlot<int> LinesPerStep = new();

    [Input(Guid = "ab4945b8-5e50-4f40-9689-839a4051f0ec")]
    public readonly InputSlot<Gradient> ColorOverLifetime = new();

    [Input(Guid = "df2076a1-1d25-4112-b0e6-2987541786d9")]
    public readonly InputSlot<Vector4> Color = new();

    [Input(Guid = "5f64292f-24ac-4a6c-ac3f-402468b0391b")]
    public readonly InputSlot<float> LineWidth = new();

    [Input(Guid = "09af14b3-b4c3-4044-b5d1-0d05ec29b6b4")]
    public readonly InputSlot<float> ScatterLookUp = new();

    [Input(Guid = "7d6b777c-3478-4dec-b2a4-dd286473a3f2")]
    public readonly InputSlot<Gradient> LineGradient = new();

    [Input(Guid = "2356bf51-8e08-49f9-8d97-fac5bb6bc25e")]
    public readonly InputSlot<int> BlendMode = new();

    [Input(Guid = "32cd717f-59aa-4fad-9a39-138177124b8c")]
    public readonly InputSlot<bool> EnableZWrite = new();

    [Input(Guid = "d719fdfa-a933-442e-90ce-70932b2a356f")]
    public readonly InputSlot<bool> Reset = new();
}