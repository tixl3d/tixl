namespace Examples.Lib.field.combine{
    [Guid("ce527990-258e-4eb6-8bba-665022c8beaf")]
    internal sealed class CombineSDFExample : Instance<CombineSDFExample>
    {
        [Output(Guid = "3a9a2366-c636-460a-a37d-645a3dfcd426")]
        public readonly Slot<Texture2D> ColorBuffer = new Slot<Texture2D>();


    }
}

