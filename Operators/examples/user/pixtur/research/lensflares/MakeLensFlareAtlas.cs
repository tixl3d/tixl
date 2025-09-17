namespace Examples.user.pixtur.research.lensflares;

[Guid("b4f1c7f1-28b4-4b3b-9227-f37b2e1e5256")]
public class MakeLensFlareAtlas : Instance<MakeLensFlareAtlas>
{
    [Output(Guid = "8d642f6c-7c6c-483f-a547-03d6e7cc645c")]
    public readonly Slot<Texture2D> Texture = new Slot<Texture2D>();

    [Input(Guid = "43c28e5c-cbe0-42c4-8172-1e9c422a22a4")]
    public readonly MultiInputSlot<float> RandomSeeds = new MultiInputSlot<float>();

    [Input(Guid = "a0b6c718-60db-474d-84b6-f6bcee78f87a")]
    public readonly InputSlot<bool> EnableUpdate = new InputSlot<bool>();

    [Input(Guid = "ddf92c4e-5d72-4d5c-b0ef-103bb9e37c9a")]
    public readonly InputSlot<int> BlurSteps = new InputSlot<int>();

    [Output(Guid = "52ca85bf-87b6-46fe-9f1c-b18543223f48")]
    public readonly Slot<Int2> Result = new Slot<Int2>();


}