namespace Skills.Image.I03_Analyze;

[Guid("696352f0-e725-4b88-85b5-e5463089fe76")]
internal sealed class I03d_InvalidColors :Instance<I03d_InvalidColors>{
    [Output(Guid = "e604cc9d-af6d-4d6b-89a2-7cc36a413fb6")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}