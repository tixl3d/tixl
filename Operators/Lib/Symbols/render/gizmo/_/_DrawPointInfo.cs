namespace Lib.render.gizmo.@_;

[Guid("ff5b93e3-d29f-489f-8cca-cb12cd1af65d")]
internal sealed class _DrawPointInfo : Instance<_DrawPointInfo>
{

    [Output(Guid = "c4f6184c-adca-4637-ba22-3ece7efbd8dc")]
    public readonly Slot<Command> Output = new();

    [Input(Guid = "44e119a8-2aa5-47b1-808c-2b207d74a0b4")]
    public readonly InputSlot<BufferWithViews> Points = new();
        
    [Input(Guid = "1F8D3E29-4EC6-4339-8463-223E8336F4D7")]
    public readonly InputSlot<BufferWithViews> OverridePositions = new();


    [Input(Guid = "e6d32fee-30d9-4ccf-b48b-88906f841ff8")]
    public readonly InputSlot<Vector2> LabelOffset = new();

    [Input(Guid = "0353313f-64dc-4e93-8bac-3c9b78df5cc9")]
    public readonly InputSlot<Vector4> Color = new();

    [Input(Guid = "776f6602-f5f6-43c9-b00a-a60e7e7454d8")]
    public readonly InputSlot<int> IndexOffset = new();

    [Input(Guid = "17b58d1a-ee78-49cf-9a71-1b339b985088", MappedType = typeof(Modes))]
    public readonly InputSlot<int> Show = new();

    [Input(Guid = "f21dfa0c-6276-47cc-8965-9060fe9d96d4")]
    public readonly InputSlot<float> Scale = new();

    // struct Point
    // {
    //     float3 Position;
    //     float FX1;
    //     float4 Rotation; //4
    //     float4 Color;    //8
    //     float3 Scale;    //12
    //     float FX2;       //15
    // };
    
    private enum Modes
    {
        Index,
        Fx1,       // 3
        Fx2,       // 15
        PositionX, // 0
        PositionY, // 1
        PositionZ, // 2
        ScaleX,    // 12 
        ScaleY,    // 13
        ScaleZ,    // 14
        ColorR,    // 8
        ColorG,    // 9
        ColorB,    // 10
        ColorA,    // 11
    }

}