namespace Skills.Image.I03_Analyze;

[Guid("7941c133-12a3-4939-ae93-b9bf0c419184")]
internal sealed class I03c_OutofRange :Instance<I03c_OutofRange>{
    [Output(Guid = "e834deaa-982c-4b15-ad74-182bb628e319")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}