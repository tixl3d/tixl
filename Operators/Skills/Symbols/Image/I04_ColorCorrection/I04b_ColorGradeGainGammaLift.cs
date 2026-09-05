namespace Skills.Image.I04_ColorCorrection;

[Guid("da246c0f-9f68-4ff4-b1aa-8163f15bb0ae")]
internal sealed class I04b_ColorGradeGainGammaLift :Instance<I04b_ColorGradeGainGammaLift>{
    [Output(Guid = "77f850f0-8df7-42de-b9c0-8d64ce6f8b2e")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}