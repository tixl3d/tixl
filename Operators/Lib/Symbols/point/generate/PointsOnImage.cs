namespace Lib.point.generate;

[Guid("722e79cc-47bc-42cc-8fce-2e06f36f8caa")]
internal sealed class PointsOnImage : Instance<PointsOnImage>
{
    [Output(Guid = "7c8567c9-1456-4040-ad43-4cc8a96efbaf")]
    public readonly Slot<BufferWithViews> OutputPoints = new();

        [Input(Guid = "5184f2ec-4f91-4dd2-9872-d9ad8d4e5d92")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> Image = new InputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "065bb5be-e5ee-4ed6-8521-a0969fcb6f4f")]
        public readonly InputSlot<bool> IsEnabled = new InputSlot<bool>();

        [Input(Guid = "5c7e5e27-2eb8-4933-97cb-fc49d576d625")]
        public readonly InputSlot<int> Count = new InputSlot<int>();

        [Input(Guid = "effde91f-2cbc-4400-b1fb-17677c538fe6")]
        public readonly InputSlot<System.Numerics.Vector2> GainAndBias = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "71d1e34f-bf8c-4e24-87b2-177bb3249b12")]
        public readonly InputSlot<float> ScatterWithinPixel = new InputSlot<float>();

        [Input(Guid = "db2ccacc-8fcd-4567-b83a-d8954dc6c217")]
        public readonly InputSlot<bool> ApplyColorToPoints = new InputSlot<bool>();

        [Input(Guid = "19db4357-97ae-4e83-8464-4e4cf9182bdb")]
        public readonly InputSlot<int> Seed = new InputSlot<int>();

        [Input(Guid = "1d0e573e-f733-4715-afe3-f96950f29aa4")]
        public readonly InputSlot<System.Numerics.Vector4> ColorWeight = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "a970dad9-e5e9-4756-a84c-86737cce7e8f")]
        public readonly InputSlot<float> ClampThreshold = new InputSlot<float>();

        [Input(Guid = "9af8bd73-43cd-4d54-b7a1-d537a880e736", MappedType = typeof(Modes))]
        public readonly InputSlot<int> Mode = new InputSlot<int>();
        
    private enum Modes
    {
        UseBrightness,
        UseSimilarity,
    }
}