namespace Skills.Render.R01_MovingThings;

[Guid("511b9fd5-49e5-4506-9162-10a1e3c6e792")]
internal sealed class R01f_SimpleTransformations :Instance<R01f_SimpleTransformations>{
    [Output(Guid = "344d2e54-5048-4414-b1c2-3102b3e19203")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}