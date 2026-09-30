namespace Examples.Lib.mesh.generate{
    [Guid("04946419-d6bd-4dab-8bd0-fb293a303e01")]
    internal sealed class TorusMeshExample : Instance<TorusMeshExample>
    {
        [Output(Guid = "1becf5fc-e456-4404-82d4-bec947a1a19f")]
        public readonly Slot<Texture2D> ColorBuffer = new Slot<Texture2D>();


    }
}

