namespace Lib.render.shading.@_;

[Guid("d062c1c2-a7d1-4d4d-a9b1-e0e96df02385")]
internal sealed class FourPointLights : Instance<FourPointLights>
{
    [Output(Guid = "27c44461-0f69-4046-9c44-cc70d4ce7818")]
    public readonly Slot<Command> Output = new();


    [Input(Guid = "30d72726-f818-4dd4-b9e6-c4c5c3509ff0")]
    public readonly InputSlot<Command> Command = new();

    [Input(Guid = "8fa9a503-76fb-4f48-b613-02cadc3c27b9")]
    public readonly InputSlot<float> Size = new();

}