namespace Skills.Render.R01_MovingThings;

[Guid("11488449-9d39-4242-bbd7-9069fa71eb61")]
internal sealed class R01g_Spreading :Instance<R01g_Spreading>{
    [Output(Guid = "c12ae506-f005-4f2e-8530-4e0f74ac7444")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}