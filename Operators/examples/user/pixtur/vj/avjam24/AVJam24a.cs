namespace Examples.user.pixtur.vj.avjam24;

[Guid("45615cbf-3cf5-46f3-b709-905b359b4362")]
public class AVJam24a : Instance<AVJam24a>
{
    [Output(Guid = "c9c264b5-e631-4e41-9823-29773e2a23f4")]
    public readonly Slot<Texture2D> ColorBuffer = new();

        [Input(Guid = "3f6d41be-25f6-46ec-bb08-b0754b952d33")]
        public readonly InputSlot<float> TestInput = new InputSlot<float>();


}