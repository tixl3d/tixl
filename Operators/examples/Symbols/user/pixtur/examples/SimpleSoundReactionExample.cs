namespace Examples.user.pixtur.examples;

[Guid("f02d4cf2-4fb0-4964-8990-2360d2db7979")]
public class SimpleSoundReactionExample : Instance<SimpleSoundReactionExample>
{

        [Output(Guid = "ce1276d0-7588-4484-883f-0b20783983ae")]
        public readonly Slot<T3.Core.DataTypes.Texture2D> ImageOutput = new Slot<T3.Core.DataTypes.Texture2D>();


}