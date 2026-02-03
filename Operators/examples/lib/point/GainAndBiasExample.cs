namespace Examples.Lib.point;

[Guid("e6d19a14-54b7-4554-8e92-9001b2530937")]
 internal sealed class GainAndBiasExample : Instance<GainAndBiasExample>
{
    [Output(Guid = "1d906c1e-77a8-4027-8896-85c59b5520ee")]
    public readonly Slot<Texture2D> ImgOutput = new Slot<Texture2D>();
        
}