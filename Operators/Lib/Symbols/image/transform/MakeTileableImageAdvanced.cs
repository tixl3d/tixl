namespace Lib.image.transform;


[Guid("613507c8-fe54-40b0-8434-d0c4b4f59b45")]
internal sealed class MakeTileableImageAdvanced : Instance<MakeTileableImageAdvanced>
{

    [Output(Guid = "3ccf32e0-8fe8-4b71-920b-c3058a2604a0")]
    public readonly Slot<Texture2D> Selected = new Slot<Texture2D>();

    [Input(Guid = "ce6c982d-8878-4fee-8f88-41d11e008ec2")]
    public readonly InputSlot<Texture2D> ImageA = new InputSlot<Texture2D>();

    [Input(Guid = "70fa51a8-b683-4a17-ad2b-d6ee5ccda3c0")]
    public readonly InputSlot<float> EdgeFallOff = new InputSlot<float>();

    [Input(Guid = "fcdeedd3-e872-4b86-975c-de7159e0d5c1")]
    public readonly InputSlot<int> TilingMode = new InputSlot<int>();

    [Input(Guid = "2876b27b-31fc-468a-9ac0-84d994a8c67c")]
    public readonly InputSlot<bool> AddNoiseToTransition = new InputSlot<bool>();

    [Input(Guid = "6f33e428-982f-4279-8b2b-0c8c2404c04d")]
    public readonly InputSlot<float> FadeOut = new InputSlot<float>();

    [Input(Guid = "9c2c30e4-3ebc-4fe3-8353-508c725a7418")]
    public readonly InputSlot<float> Scale = new InputSlot<float>();

    [Input(Guid = "8d45de22-edfa-4f34-8030-2b038666b501")]
    public readonly InputSlot<float> Phase = new InputSlot<float>();

}