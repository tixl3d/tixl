namespace Examples.user.pixtur.research;

[Guid("c454abd8-62a8-4413-a463-668013a6a5bd")]
public class RaymarchFractal : Instance<RaymarchFractal>
{
    [Output(Guid = "da8c003a-893f-402e-a8c2-d92b41d4ea00")]
    public readonly Slot<Texture2D> TextureOutput = new();

    [Output(Guid = "5bb98ed6-8d29-428f-b5c3-1953df353c8c")]
    public readonly Slot<Command> DrawCommand = new();

    [Input(Guid = "34805b11-c0d5-40da-945d-5282fcb48921")]
    public readonly InputSlot<float> MaxSteps = new();

    [Input(Guid = "bef2f121-5469-40f1-8e53-3961ccc9092d")]
    public readonly InputSlot<float> StepSize = new();

    [Input(Guid = "0bdd1ad2-b2a0-4051-9278-cb71bc9b3fc7")]
    public readonly InputSlot<float> MinDistance = new();

    [Input(Guid = "18a62f78-d62b-4d90-aed6-48629ac869df")]
    public readonly InputSlot<float> MaxDistance = new();

    [Input(Guid = "6b8a364e-d5a5-4431-8b0d-cd790b56cde4")]
    public readonly InputSlot<float> Minrad = new();

    [Input(Guid = "31a76632-a1b4-4366-9e49-d92f5bb076ae")]
    public readonly InputSlot<float> Scale = new();

    [Input(Guid = "74462bd0-6021-4643-b63d-471ad5f10bd7")]
    public readonly InputSlot<System.Numerics.Vector2> Fold = new();

    [Input(Guid = "2125e347-e3d7-41fd-b68a-9c0a807c3c2b")]
    public readonly InputSlot<System.Numerics.Vector3> Clamping = new();

    [Input(Guid = "9ec855e3-5d7c-47d9-a7c1-e38c8349eb94")]
    public readonly InputSlot<System.Numerics.Vector3> Increment = new();

    [Input(Guid = "f324c2f4-5042-448f-b795-aa2077e0588f")]
    public readonly InputSlot<System.Numerics.Vector4> Surface1 = new();

    [Input(Guid = "a14da06a-3ac0-498c-a648-965a8d555f8d")]
    public readonly InputSlot<System.Numerics.Vector4> Surface2 = new();

    [Input(Guid = "7993af9a-71e2-4fbc-aba8-706130b1bfe2")]
    public readonly InputSlot<System.Numerics.Vector4> Surface3 = new();

    [Input(Guid = "30ae2bd7-36e6-46fe-afe5-5d14182782cd")]
    public readonly InputSlot<System.Numerics.Vector4> Specular = new();

    [Input(Guid = "9e0b68a1-795a-4122-ba9b-ec830d590b51")]
    public readonly InputSlot<System.Numerics.Vector4> Glow_ = new();

    [Input(Guid = "d0b9b178-1028-48bc-ad43-3175ed77ee97")]
    public readonly InputSlot<System.Numerics.Vector4> AmbientOcclusion = new();

    [Input(Guid = "efe14079-a0da-40e2-9463-e6bdaa338d4f")]
    public readonly InputSlot<System.Numerics.Vector4> Background = new();

    [Input(Guid = "d39179c3-d77b-4990-a7fa-8abaf97e920a")]
    public readonly InputSlot<System.Numerics.Vector2> Spec = new();

    [Input(Guid = "986b63f8-8b33-49ff-a9d1-7c5de88f0683")]
    public readonly InputSlot<float> AoDistance = new();

    [Input(Guid = "1ba46b64-2e69-484e-b5e9-08f4d69d65b0")]
    public readonly InputSlot<float> Fog = new();

    [Input(Guid = "9b61bb35-fb53-49d8-84cd-d91218f55eb5")]
    public readonly InputSlot<System.Numerics.Vector3> LightPos = new();

    [Input(Guid = "8f697ed3-b5a3-4fd3-83d7-c70fe5bf89c7")]
    public readonly InputSlot<float> DistToColor = new();

    [Input(Guid = "f9cc2427-135d-492e-b25d-276715ab82f3")]
    public readonly InputSlot<Texture2D> Image = new();

    [Input(Guid = "3522be02-01e2-4ac4-8e73-bf81eeb71cf0")]
    public readonly InputSlot<Int2> Resolution = new();
}