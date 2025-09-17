namespace Examples.user.pixtur.dailies;

[Guid("6f9126f8-fed5-4088-905a-7be8e800b968")]
public class SlotMachine : Instance<SlotMachine>
{
    [Output(Guid = "f079137c-1c94-49a6-ab95-6f54a7354183")]
    public readonly Slot<Texture2D> ImgOutput = new();

    [Input(Guid = "aff18f50-3338-43fa-9e4f-4f2fbe73285d")]
    public readonly InputSlot<bool> BoolValue = new();


}