using T3.Core.Utils;

namespace Lib.point.draw;

[Guid("7f69a5e5-28e5-44c1-b3e3-74b05faa0531")]
internal sealed class DrawRayLines : Instance<DrawRayLines>
{
    [Output(Guid = "2f62657b-0f4b-458b-b504-0e9dc6b29dcb")]
    public readonly Slot<Command> Output = new();

    [Input(Guid = "fd05097a-2842-464a-b8d4-1479adb7785d")]
    public readonly InputSlot<BufferWithViews> GPoints = new();

    [Input(Guid = "c7f51f64-e473-4780-8659-56e85b9ed219")]
    public readonly InputSlot<Vector4> Color = new();

    [Input(Guid = "20dfe824-f1d1-4eb2-a9cb-a3240195360a")]
    public readonly InputSlot<float> Size = new();

    [Input(Guid = "7340e3bd-b1d2-4aa3-b8d0-0e941751b211")]
    public readonly InputSlot<float> ShrinkWithDistance = new();

    [Input(Guid = "e78179bf-6ba9-4eda-9989-c9de16d2db62")]
    public readonly InputSlot<float> TransitionProgress = new();

    [Input(Guid = "72db5734-f2bc-445f-9d96-61a4fb81995c")]
    public readonly InputSlot<float> UseWForWidth = new();

    [Input(Guid = "14497750-82ec-40a9-b38b-b62813ee93dc")]
    public readonly InputSlot<bool> UseWAsTexCoordV = new();

    [Input(Guid = "47667ddc-1bbb-4686-9144-ce218172b5ec")]
    public readonly InputSlot<Texture2D> Texture_ = new();

    [Input(Guid = "f59b07c9-f947-4ab5-96c7-20801aca8d1a")]
    public readonly InputSlot<bool> EnableTest = new();

    [Input(Guid = "d8c585bc-8bab-412f-94bf-dc6d8d5643b3")]
    public readonly InputSlot<bool> EnableDepthWrite = new();

    [Input(Guid = "037a162b-2054-44b1-b536-b532ab0c14b7", MappedType = typeof(SharedEnums.BlendModes))]
    public readonly InputSlot<int> BlendMod = new();

        [Input(Guid = "ee15ed79-2c1e-4d21-9509-aa021d3a9115")]
        public readonly InputSlot<int> WidthFactor = new InputSlot<int>();
}