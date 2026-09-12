namespace Types.Routing;

// Passes T3.Core.DataTypes.BufferWithViews through the .t3 connection; no C# evaluator is needed.
[Guid("33df134e-2e82-49e4-a259-18a6f33791db")]
public sealed class RerouteBufferWithViews : Instance<RerouteBufferWithViews>, IRerouteNode
{
    [Output(Guid = "4a9e15fa-ebe5-4778-a9b9-b88170389904")]
    public readonly Slot<T3.Core.DataTypes.BufferWithViews> Output = new();

    [Input(Guid = "e4f90dcf-9320-47ab-b7c0-36c96263c044")]
    public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> Input = new();
}
