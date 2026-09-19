namespace Examples.user.anosci;

[Guid("9e243276-1807-49f7-9e78-29b582aafb39")]
internal sealed class PlayAtlas : Instance<PlayAtlas>
{

    [Input(Guid = "6bffaa67-be6d-43cc-9890-75d0a1c51d75")]
    public readonly InputSlot<Texture2D> Texture2d = new InputSlot<Texture2D>();

    [Input(Guid = "a47fc959-4ffa-4790-ac1f-5097a72fab3f")]
    public readonly InputSlot<int> FrameCountX = new InputSlot<int>();

    [Input(Guid = "25ac515f-2f7f-4ebf-9852-fc96bc83d6af")]
    public readonly InputSlot<int> FrameCountY = new InputSlot<int>();

    [Input(Guid = "65f7f342-fbe8-4fb3-9019-c891fa651d5d")]
    public readonly InputSlot<int> FramesToTruncate = new InputSlot<int>();

    [Input(Guid = "7304732c-ebfa-41d5-938c-f01ecc228749")]
    public readonly InputSlot<bool> DisableSnapping = new InputSlot<bool>();

    [Input(Guid = "3f7b1f1f-7790-4142-890c-c69a17689b87")]
    public readonly InputSlot<int> StartFrame = new InputSlot<int>();

    [Input(Guid = "69c0ee10-8498-4128-b865-7e622b62cd3d")]
    public readonly InputSlot<float> PlaybackRate = new InputSlot<float>();

    [Input(Guid = "21212842-58f4-424b-98c2-603423fead1f")]
    public readonly InputSlot<float> FineDelay = new InputSlot<float>();

    [Output(Guid = "372da297-6bb0-49fc-a92f-41bfcc5f9f1a")]
    public readonly Slot<Texture2D> output = new Slot<Texture2D>();

}