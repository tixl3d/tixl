namespace Examples.user.pixtur.learning.cs._07_CCAInstancing;

[Guid("7f40b06a-d5f9-490a-9acc-24eff78e3b40")]
public class _CCAInstancing : Instance<_CCAInstancing>
{
    [Output(Guid = "f9144007-9d95-459d-8a0e-62489fb76a91")]
    public readonly Slot<Texture2D> Output = new();

    [Input(Guid = "cd6ce054-b3ed-443b-85e2-8e35c578afc9")]
    public readonly InputSlot<float> Threshold = new();

    [Input(Guid = "ed0a2cdb-df5e-42a1-93c6-28104c6366fd")]
    public readonly InputSlot<float> MaxSteps = new();

    [Input(Guid = "ab4b4b7a-e15b-49d5-8287-ea734c484629")]
    public readonly InputSlot<float> Range = new();

    [Input(Guid = "d94db85b-5fb4-47a6-821a-7ff3cc92dca6")]
    public readonly InputSlot<float> RandomAmount = new();

    [Input(Guid = "06c20555-c606-4dcd-9050-04c85a0b6098")]
    public readonly InputSlot<bool> AddNoise2 = new();

    [Input(Guid = "523436c5-397b-401a-9060-f3c6fd070f44")]
    public readonly InputSlot<float> R_xThreshold = new();

    [Input(Guid = "aa684bed-dac0-4358-9c2b-e1d37dda04d7")]
    public readonly InputSlot<float> G_xStates = new();

    [Input(Guid = "81055dac-d6f0-4b80-90dd-94b517e55fe8")]
    public readonly InputSlot<bool> UseMooreRegion = new();

    [Input(Guid = "d010338b-9685-4256-bbc0-afeb16cb4363")]
    public readonly InputSlot<Int2> PointGridRez = new();

}