namespace Lib.render.postfx;

[Guid("8e2f6b14-a7c3-4d59-b0e8-3c91f5d27a66")]
internal sealed class ScreenSpaceAO : Instance<ScreenSpaceAO>
{
    [Output(Guid = "9f307c25-b8d4-4e6a-81f9-4da206e38b77")]
    public readonly Slot<Texture2D> Output = new();

    [Output(Guid = "a0418d36-c9e5-4f7b-920a-5eb317f49c88")]
    public readonly Slot<Texture2D> Occlusion = new();

    [Input(Guid = "b1529e47-daf6-408c-a31b-6fc42805ad99")]
    public readonly InputSlot<Texture2D> Image = new();

    [Input(Guid = "c263af58-eb07-419d-b42c-70d53916beaa")]
    public readonly InputSlot<Texture2D> DepthMap = new();

    [Input(Guid = "d374b069-fc18-42ae-853d-81e64a27cfbb")]
    public readonly InputSlot<Texture2D> NormalMap = new();

    [Input(Guid = "e485c17a-0d29-43bf-964e-92f75b38d0cc")]
    public readonly InputSlot<Object> CameraReference = new();

    [Input(Guid = "f596d28b-1e3a-44c0-a75f-a3086c49e1dd")]
    public readonly InputSlot<float> Intensity = new();

    [Input(Guid = "06a7e39c-2f4b-45d1-b860-b4197d5af2ee")]
    public readonly InputSlot<float> Radius = new();

    [Input(Guid = "17b8f4ad-305c-46e2-8971-c52a8e6b03ff")]
    public readonly InputSlot<float> Thickness = new();

    [Input(Guid = "28c905be-416d-47f3-9a82-d63b9f7c1400")]
    public readonly InputSlot<int> RaysPerPixel = new();

    [Input(Guid = "39da16cf-527e-4804-ab93-e74ca08d2511")]
    public readonly InputSlot<int> StepsPerRay = new();

    [Input(Guid = "4aeb27d0-638f-4915-bca4-f85db19e3622")]
    public readonly InputSlot<float> BlendFactor = new();

    [Input(Guid = "c59e38d7-204b-4fa1-b6e8-7d13f9a02c54")]
    public readonly InputSlot<int> MaxAccumulatedFrames = new();

    [Input(Guid = "5bfc38e1-7490-4a26-8db5-096ec2af4733")]
    public readonly InputSlot<float> NeighborhoodClamp = new();

    [Input(Guid = "6c0d49f2-85a1-4b37-9ec6-1a7fd3b05844")]
    public readonly InputSlot<float> DepthRejection = new();

    [Input(Guid = "7d1e5a03-96b2-4c48-afd7-2b80e4c16955")]
    public readonly InputSlot<bool> Reset = new();
}
