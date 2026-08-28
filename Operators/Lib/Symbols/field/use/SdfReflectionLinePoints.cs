namespace Lib.field.use;

[Guid("4af7bbd1-9231-4ef3-bb7a-1d643c7d16cf")]
internal sealed class SdfReflectionLinePoints :Instance<SdfReflectionLinePoints>{
    [Output(Guid = "47add549-192a-4771-8f03-cdd0e90d4e72")]
    public readonly Slot<BufferWithViews> Result2 = new();

        [Input(Guid = "c4e2297a-dd91-4162-8f2c-e968d8529158")]
        public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> Points = new InputSlot<T3.Core.DataTypes.BufferWithViews>();

        [Input(Guid = "5613c80e-a0a5-4996-a864-362d021fc056")]
        public readonly InputSlot<T3.Core.DataTypes.ShaderGraphNode> Field = new InputSlot<T3.Core.DataTypes.ShaderGraphNode>();

        [Input(Guid = "0d73ebd5-c76b-46c2-b15f-96ff2ec498bd")]
        public readonly InputSlot<int> MaxReflectionCount = new InputSlot<int>();

        [Input(Guid = "c444a71a-5760-49a7-bfc8-3b97449a0343")]
        public readonly InputSlot<int> MaxSteps = new InputSlot<int>();

        [Input(Guid = "403f823d-043b-4009-b00f-e90aa154f30f")]
        public readonly InputSlot<float> MinDistance = new InputSlot<float>();

        [Input(Guid = "a3d0717d-7afd-48cb-a43c-72701bcc1393")]
        public readonly InputSlot<float> StepDistanceFactor = new InputSlot<float>();

        [Input(Guid = "818886bf-cc7d-49ce-9294-fda1d6a17177", MappedType = typeof(WriteDistanceModes))]
        public readonly InputSlot<int> WriteDistanceTo = new InputSlot<int>();

        [Input(Guid = "c7a5d340-cf58-43dd-9bef-eb8c417e9d2e", MappedType = typeof(WriteDistanceModes))]
        public readonly InputSlot<int> WriteStepCountTo = new InputSlot<int>();

        [Input(Guid = "c33c3db6-fb51-4ed3-b986-5580d2ceb539")]
        public readonly InputSlot<float> NormalSamplingDistance = new InputSlot<float>();

        [Input(Guid = "94c84de7-ac0c-48c1-9a45-f2d39ab6513f")]
        public readonly InputSlot<float> MaxDistance = new InputSlot<float>();
        
    private enum WriteDistanceModes
    {
        None,
        FX1,
        FX2,
    }

    private enum Modes
    {
        Raymarch,
        KeepSteps,
    }
}