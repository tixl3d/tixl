namespace Examples.user.pixtur.learning.cs._07_CCAInstancing;

[Guid("63090462-3237-465e-a12c-25c8bbfaaa8c")]
public class CASim2d : Instance<CASim2d>
{

    [Output(Guid = "b4059e4a-8238-4f37-ab2d-d45d7853cff9")]
    public readonly Slot<Texture2D> CABuffer2 = new();

    [Input(Guid = "69b5c584-f365-469c-bf08-b1a125f7d757")]
    public readonly InputSlot<Int2> Resolution = new();

    [Input(Guid = "0f5159f9-7577-4e02-85f2-73f7566b897d")]
    public readonly InputSlot<int> States = new();

    [Input(Guid = "6374d267-e1a4-418e-8463-bba8a512708b")]
    public readonly InputSlot<int> Neighbours = new();

    [Input(Guid = "5f13eb52-23d4-4a47-b7cc-ea6ba74062ed")]
    public readonly InputSlot<int> RandomSeed = new();

    [Input(Guid = "1707a83c-bfab-40ef-af39-cc7249505a91")]
    public readonly InputSlot<bool> Reset = new();

    [Input(Guid = "7c5feb4a-9611-46b0-af48-8675d4c618b9")]
    public readonly InputSlot<int> SlowDown = new();

    [Input(Guid = "3b633979-3ee0-4b09-a04e-6bf2287357f6")]
    public readonly InputSlot<float> Lambda = new();

    [Input(Guid = "546b5816-8783-4f1e-9907-0e8414e34606")]
    public readonly InputSlot<bool> Isotropic = new();

    [Input(Guid = "7ce33028-ecd0-4e6b-b857-52eb8bbec9f4")]
    public readonly InputSlot<bool> ResetOnChange = new();

    [Input(Guid = "9d58ecd1-7ed1-43d2-83f4-1710e4258822")]
    public readonly InputSlot<Texture2D> FxTexture = new();

    [Input(Guid = "eb02b152-63ad-4227-ba5d-6baa9ce9884a")]
    public readonly InputSlot<float> FxThreshold = new();

}