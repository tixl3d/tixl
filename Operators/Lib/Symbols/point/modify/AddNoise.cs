namespace Lib.point.modify;

[Guid("dd586355-64b3-4e96-af6d-b4927595dee7")]
internal sealed class AddNoise : Instance<AddNoise>
{

    [Output(Guid = "bea6aa18-e751-4ce7-b7d7-b7a026c8e019")]
    public readonly Slot<BufferWithViews> Output = new();

        [Input(Guid = "3f5abde2-66e1-4b04-9bff-5a19a58aab86")]
        public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> Points = new InputSlot<T3.Core.DataTypes.BufferWithViews>();

        [Input(Guid = "5894156a-cc31-4236-908c-de0e5385fd84")]
        public readonly InputSlot<float> Strength = new InputSlot<float>();

        [Input(Guid = "96b296e9-f15a-4543-a850-8078c583ee94", MappedType = typeof(FModes))]
        public readonly InputSlot<int> StrengthFactor = new InputSlot<int>();

        [Input(Guid = "929db7b2-f19c-4a28-b4c2-187365b99760")]
        public readonly InputSlot<float> Frequency = new InputSlot<float>();

        [Input(Guid = "aaba1602-e7a1-4b48-81d4-9d7b2b3aa8b1")]
        public readonly InputSlot<float> Phase = new InputSlot<float>();

        [Input(Guid = "1dfb45ae-b376-41ea-a1d2-97b170645b50")]
        public readonly InputSlot<float> Variation = new InputSlot<float>();

        [Input(Guid = "c2df1fa3-88e1-4be2-954e-8c44edd9d421")]
        public readonly InputSlot<System.Numerics.Vector3> AmountDistribution = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "97c25ec6-ef71-42f8-9352-52baf2ce41a4")]
        public readonly InputSlot<float> RotationLookupDistance = new InputSlot<float>();

        [Input(Guid = "2d229dc7-a7ec-4fae-b84c-5eea6b91e18a")]
        public readonly InputSlot<System.Numerics.Vector3> NoiseOffset = new InputSlot<System.Numerics.Vector3>();
        
        private enum FModes
        {
            None = 0,
            F1 = 1,
            F2 = 2,
        }
}