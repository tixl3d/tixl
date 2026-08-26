namespace Lib.render.@_;

[Guid("45ddd1ac-5fd8-4479-81ac-eae5baf71fb6")]
internal sealed class ComputeImageDifference : Instance<ComputeImageDifference>
{

        [Input(Guid = "c0438cca-a773-401c-9cc2-700c6d53cbcf")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> ImageA = new InputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "1aade534-94f4-4bd0-bc73-b73ad9d666fb")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> ImageB = new InputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "2713907a-425e-44d2-ab38-33570f52296c")]
        public readonly InputSlot<float> PrecisionScale = new InputSlot<float>();

    private enum Modes
    {
        Linear,
        LegacyDOF,
    }

        [Output(Guid = "3007edc1-9cbe-43fb-b840-46beb475220d")]
        public readonly Slot<float> Difference = new Slot<float>();
}