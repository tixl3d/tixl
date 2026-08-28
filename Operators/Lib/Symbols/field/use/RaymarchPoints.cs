namespace Lib.field.use;

[Guid("a914c462-b707-4240-b57b-ed0e24e80a94")]
internal sealed class RaymarchPoints : Instance<RaymarchPoints>
{
    [Output(Guid = "8b815a36-f585-454e-b3b0-126200994c60")]
    public readonly Slot<BufferWithViews> Result2 = new();

        [Input(Guid = "a21ebc40-ae79-439b-a3e7-d90a50b71ca7")]
        public readonly InputSlot<T3.Core.DataTypes.ShaderGraphNode> Field = new InputSlot<T3.Core.DataTypes.ShaderGraphNode>();

        [Input(Guid = "b1dc8336-33e0-4856-ba18-eaac052c5f43")]
        public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> Points = new InputSlot<T3.Core.DataTypes.BufferWithViews>();

        [Input(Guid = "71d7f5a5-f13c-40fd-9a28-fbdd64ae07b6")]
        public readonly InputSlot<int> MaxSteps = new InputSlot<int>();

        [Input(Guid = "000617c3-709d-49e1-953e-55d45b565504")]
        public readonly InputSlot<float> MinDistance = new InputSlot<float>();

        [Input(Guid = "81855333-ab3a-4543-b68c-0eb2f62427b0")]
        public readonly InputSlot<float> StepDistanceFactor = new InputSlot<float>();

        [Input(Guid = "86fbda4c-5c1b-42b0-a35f-ebcccd6cc648")]
        public readonly InputSlot<int> MaxReflectionCount = new InputSlot<int>();

        [Input(Guid = "75b28b25-02bf-4926-8d27-2a547231f40a", MappedType = typeof(Modes))]
        public readonly InputSlot<int> Mode = new InputSlot<int>();

        [Input(Guid = "cf27f634-039e-4500-acdb-b0523ea2d198", MappedType = typeof(WriteDistanceModes))]
        public readonly InputSlot<int> WriteDistanceTo = new InputSlot<int>();

        [Input(Guid = "685b1c07-4696-4a87-bb24-3103122db557", MappedType = typeof(WriteDistanceModes))]
        public readonly InputSlot<int> WriteStepCountTo = new InputSlot<int>();

        [Input(Guid = "a8f8ed3f-01e4-48c9-beaa-4a2833d50863")]
        public readonly InputSlot<float> NormalSamplingDistance = new InputSlot<float>();

        [Input(Guid = "3ea2d4c9-35f4-49d4-b26f-1e12bfd71fbe")]
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