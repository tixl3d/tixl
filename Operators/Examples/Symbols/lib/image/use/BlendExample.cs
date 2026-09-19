namespace Examples.Lib.image.use{
    [Guid("40c5312c-9684-4821-8b05-e8d6c40da07b")]
    internal sealed class BlendExample : Instance<BlendExample>
    {
        [Output(Guid = "473cdfc7-7f34-4567-88f4-54e8f5120b1b")]
        public readonly Slot<Texture2D> ColorBuffer = new Slot<Texture2D>();


    }
}

