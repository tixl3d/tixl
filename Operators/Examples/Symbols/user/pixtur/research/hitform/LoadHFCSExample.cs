namespace Examples.user.pixtur.research.hitform;

[Guid("6f3d09b4-5b3e-44cf-b023-7db157682897")]
public class LoadHFCSExample : Instance<LoadHFCSExample>
{
    [Output(Guid = "8492ef4f-d86f-466c-873e-9ebbd4490cc3")]
    public readonly Slot<Texture2D> ColorBuffer = new Slot<Texture2D>();

    [Input(Guid = "b6b5617b-dada-4a58-a4df-f8f016ecbf00")]
    public readonly InputSlot<string> Path = new InputSlot<string>();


}