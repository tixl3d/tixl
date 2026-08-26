namespace Skills.Render.R07_SwitchingScenes;

[Guid("a401f4a5-ccc4-4218-a433-6b854adc6fa7")]
internal sealed class R07a_SwitchingScenes :Instance<R07a_SwitchingScenes>{
    [Output(Guid = "80dc5397-7f16-4581-b58c-7843c5272ab9")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}