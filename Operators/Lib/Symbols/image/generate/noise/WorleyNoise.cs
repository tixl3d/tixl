namespace Lib.image.generate.noise;

[Guid("5cf7a1e2-7369-4e74-a7a9-b0eae61bdc21")]
internal sealed class WorleyNoise : Instance<WorleyNoise>
{
    [Output(Guid = "59ca5836-cd4a-4c2d-b721-c392c332f798")]
    public readonly Slot<Texture2D> TextureOutput = new ();

        [Input(Guid = "7c4d90e2-a31e-4663-a4bd-da9be8fbdaf4")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> Texture = new InputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "57903730-3c40-41c0-96a2-c0127d8281c4")]
        public readonly InputSlot<float> TextureBlend = new InputSlot<float>();

        [Input(Guid = "a9111412-3369-4848-af48-8a96f5697c84")]
        public readonly InputSlot<System.Numerics.Vector4> ColorA = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "f9899540-e495-41cb-87da-302d1bf090ac")]
        public readonly InputSlot<System.Numerics.Vector4> ColorB = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "f4a32527-da4d-49af-aeb0-ed9c5c28ac04")]
        public readonly InputSlot<float> Scale = new InputSlot<float>();

        [Input(Guid = "1607d859-89e9-44b3-a92e-135c562d302e")]
        public readonly InputSlot<System.Numerics.Vector2> Stretch = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "f29bca1f-d563-4dd3-8386-ab791f4b423e")]
        public readonly InputSlot<System.Numerics.Vector2> Offset = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "37fa6406-e29c-482d-9682-70cb2faea92d")]
        public readonly InputSlot<float> Phase = new InputSlot<float>();

        [Input(Guid = "6f82fdd2-5dd6-447a-850b-e301abd35bae")]
        public readonly InputSlot<float> Randomness = new InputSlot<float>();

        [Input(Guid = "bb34cad3-1676-4df1-86f3-d713df5ccda6")]
        public readonly InputSlot<System.Numerics.Vector2> Clamping = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "dce72235-839e-41f1-a2a4-e19f0ef66e5c")]
        public readonly InputSlot<System.Numerics.Vector2> GainAndBias = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "b4277cab-d286-410f-81be-ff4d53a9eca9", MappedType = typeof(Methods))]
        public readonly InputSlot<int> Method = new InputSlot<int>();

        [Input(Guid = "7b1a1f18-a1d4-4737-9c06-21f263277140")]
        public readonly InputSlot<T3.Core.DataTypes.Vector.Int2> Resolution = new InputSlot<T3.Core.DataTypes.Vector.Int2>();

        [Input(Guid = "7b7e1f3b-7e5c-46af-9e2e-536cb1909867")]
        public readonly InputSlot<bool> GenerateMips = new InputSlot<bool>();


    private enum Methods
    {   
        Worley_F1,
        Manhattan_worley_F1,
        Chebyshev_worley_F1 ,
        Worley_F2_F1,
        Manhattan_worley_F2_F1,
        Chebyshev_worley_F2_F1
    }
}