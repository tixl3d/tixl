namespace Lib.particle.force;

[Guid("ed0bc47a-31ef-400b-b4e4-5552a859b309")]
internal sealed class CollisionForce : Instance<CollisionForce>
{

    [Output(Guid = "8c9461b8-9152-4389-938e-2ff67a6451ed")]
    public readonly Slot<T3.Core.DataTypes.ParticleSystem> Particles = new();

    [Input(Guid = "0724bf0c-8f97-44de-bf42-6a89b89f1632")]
    public readonly InputSlot<float> CellSize = new();

    [Input(Guid = "d0036a34-d7e4-4542-b231-5d1b687b0028")]
    public readonly InputSlot<float> Bounciness = new();

    [Input(Guid = "91966b2c-342a-420a-bad1-cbcf4d6af1ae")]
    public readonly InputSlot<float> Attraction = new();

    [Input(Guid = "78566405-8dee-4661-9cb7-489c8d322f64")]
    public readonly InputSlot<bool> IsEnabled = new();

    [Input(Guid = "5b3fcf3c-7155-401f-b3ac-3a4ac9d921df")]
    public readonly InputSlot<float> AttractionDecay = new();

    [Input(Guid = "91459607-22c1-4536-a384-8b1f1d8ecb64")]
    public readonly InputSlot<float> CollistionResolve = new();
}