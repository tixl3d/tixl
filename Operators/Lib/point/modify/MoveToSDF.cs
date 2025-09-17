namespace Lib.point.modify;

[Guid("983d8a34-e225-456b-9f12-806eede743e3")]
internal sealed class MoveToSDF : Instance<MoveToSDF>
{
    [Output(Guid = "d4009672-7c93-4962-81d7-656cbe70de2e")]
    public readonly Slot<BufferWithViews> Result2 = new();

        [Input(Guid = "11c1db02-0897-4a87-85c5-c9aced73ed54")]
        public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> Points = new InputSlot<T3.Core.DataTypes.BufferWithViews>();

        [Input(Guid = "e153cc10-a554-4e18-bece-691956df573f")]
        public readonly InputSlot<T3.Core.DataTypes.ShaderGraphNode> Field = new InputSlot<T3.Core.DataTypes.ShaderGraphNode>();

        [Input(Guid = "e024f44c-3ebd-4972-ae4f-43b5305df425")]
        public readonly InputSlot<float> Amount = new InputSlot<float>();

        [Input(Guid = "f7861f70-9eeb-42aa-93b5-230f03f645be")]
        public readonly InputSlot<int> MaxSteps = new InputSlot<int>();

        [Input(Guid = "cc76145b-dc10-4c36-b45b-2549c026cb12")]
        public readonly InputSlot<float> MinDistance = new InputSlot<float>();

        [Input(Guid = "42e46e25-0d3b-40cb-a32c-fb9af373220d")]
        public readonly InputSlot<float> NormalSamplingDistance = new InputSlot<float>();

        [Input(Guid = "d48504a4-9826-4166-86a3-528290796a03")]
        public readonly InputSlot<float> StepDistanceFactor = new InputSlot<float>();

        [Input(Guid = "01a17268-d6d0-4243-b5e5-0217db137ba6", MappedType = typeof(WriteDistanceModes))]
        public readonly InputSlot<int> WriteDistanceMode = new InputSlot<int>();

        [Input(Guid = "03bdab33-35a1-410b-b032-61efa1dffb9c")]
        public readonly InputSlot<bool> SetOrientation = new InputSlot<bool>();

        [Input(Guid = "168d56f3-fbe2-40a6-b9a1-f85f37a16b79", MappedType = typeof(WriteDistanceModes))]
        public readonly InputSlot<int> AmountFactor = new InputSlot<int>();

        [Input(Guid = "f8f3e984-421b-4e1e-abb1-8d75afb5b64d")]
        public readonly InputSlot<bool> SetColor = new InputSlot<bool>();
        
    private enum WriteDistanceModes
    {
        None,
        FX1,
        FX2,
    }
}