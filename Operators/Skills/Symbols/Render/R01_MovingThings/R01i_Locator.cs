namespace Skills.Render.R01_MovingThings;

[Guid("975ed32d-eedc-4208-a217-924231d339bd")]
internal sealed class R01i_Locator :Instance<R01i_Locator>{
    [Output(Guid = "067d2930-bc89-4507-a582-eec3d6e26b2b")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}