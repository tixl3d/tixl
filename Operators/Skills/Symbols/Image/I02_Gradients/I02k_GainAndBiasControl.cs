namespace Skills.Image.I02_Gradients;

[Guid("6681a029-e23f-4c6b-aa95-df30906ea58c")]
internal sealed class I02k_GainAndBiasControl : Instance<I02k_GainAndBiasControl>
{
    [Output(Guid = "c32ad1b1-dfce-4ad3-a505-dc253363501c")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}