namespace Examples.howto{
    [Guid("dd0e8f4c-99bc-4e90-965c-d2d461d4a8fe")]
    internal sealed class HowToUseColors : Instance<HowToUseColors>
    {
        [Output(Guid = "71a68dbd-0754-44e0-8b56-d1ecd8ac74f2")]
        public readonly Slot<Texture2D> TextureOutput = new Slot<Texture2D>();


    }
}

