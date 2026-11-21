namespace Lib.volume.render;

/// Raymarches up to three NanoVDB grids - a liquid level set, smoke and fire - inside a glass box, straight from the GPU buffers of [VolumeToNanoVdb].
[Guid("d3f006fd-b18b-5d4b-9ef9-1dd19653a4a5")]
internal sealed class NanoVdbRender : Instance<NanoVdbRender>
{
    [Output(Guid = "a6455792-c57f-5579-952a-c2a7818c9955")]
    public readonly Slot<Command> DrawCommand = new();

    [Input(Guid = "74e636af-eec7-51a4-89a6-bb851f98938f")]
    public readonly InputSlot<ShaderResourceView> Liquid = new();

    [Input(Guid = "5b6564b0-a48e-53c7-ae8a-ed1f362aa26d")]
    public readonly InputSlot<ShaderResourceView> Smoke = new();

    [Input(Guid = "0f98affe-42d1-5d56-98ba-ed5bdd948ced")]
    public readonly InputSlot<ShaderResourceView> Fire = new();

    [Input(Guid = "cd5b7c56-40b8-5f45-b736-e2448a20af1a")]
    public readonly InputSlot<System.Numerics.Vector3> BoxMin = new();

    [Input(Guid = "c722d77b-1aca-5bf9-a22a-255b6feb87ab")]
    public readonly InputSlot<System.Numerics.Vector3> BoxMax = new();

    [Input(Guid = "f115a2e9-1fa8-5f78-983a-6bb8bd6b61bf")]
    public readonly InputSlot<float> Time = new();

    [Input(Guid = "76f6689f-4b17-5daa-b078-1f2a176c7f03")]
    public readonly InputSlot<float> StepCount = new();

    [Input(Guid = "9e36533d-0334-5aab-880b-7799fbd6bffa")]
    public readonly InputSlot<System.Numerics.Vector3> LightDirection = new();

    [Input(Guid = "b9044242-4e5e-556f-a09f-b63482dfbce7")]
    public readonly InputSlot<float> LiquidAmount = new();

    [Input(Guid = "f0396726-d4ad-55af-9009-b7ad8ce82d7a")]
    public readonly InputSlot<System.Numerics.Vector4> LiquidColor = new();

    [Input(Guid = "6caab367-83de-56dd-99f8-4ee1264f1427")]
    public readonly InputSlot<System.Numerics.Vector4> SmokeColor = new();

    [Input(Guid = "1d6e5b85-380e-5c4f-927a-2edeea989d3c")]
    public readonly InputSlot<float> SmokeDensity = new();

    [Input(Guid = "c1140119-00fa-56d4-8f71-6bb4a79366a8")]
    public readonly InputSlot<float> FireIntensity = new();

    [Input(Guid = "7a34beba-0bdb-5c08-a8ce-3e2cfdfac56f")]
    public readonly InputSlot<float> SmokeSoftness = new();

    [Input(Guid = "2d6c8cb1-4099-58a1-8075-1ff545926c5f")]
    public readonly InputSlot<float> FireSoftness = new();

    [Input(Guid = "18b26691-6ea7-5f60-b162-aaf029bb271c")]
    public readonly InputSlot<float> NoiseAmount = new();

    [Input(Guid = "4871a6d8-10bb-58a9-9fa3-f5bba233e122")]
    public readonly InputSlot<System.Numerics.Vector4> EdgeColor = new();
}
