namespace Lib.point.generate;

[Guid("780edb20-f83f-494c-ab17-7015e2311250")]
internal sealed class RepeatAtPoints : Instance<RepeatAtPoints>
{

    [Output(Guid = "3ac76b2a-7b1c-4762-a3f6-50529cd42fa8")]
    public readonly Slot<BufferWithViews> OutBuffer = new();

        [Input(Guid = "a952d91a-a86b-4370-acd9-e17b19025966")]
        public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> GTargets = new InputSlot<T3.Core.DataTypes.BufferWithViews>();

        [Input(Guid = "47c3c549-78bb-41fd-a88c-58f643870b40")]
        public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> GPoints = new InputSlot<T3.Core.DataTypes.BufferWithViews>();

        [Input(Guid = "796d3d55-32b3-436e-a4c3-f15e1585a914", MappedType = typeof(ConnectionModes))]
        public readonly InputSlot<int> CombineMode = new InputSlot<int>();

        [Input(Guid = "9df1f57c-a079-49c1-b537-d8eb08f2d0d3")]
        public readonly InputSlot<float> Scale = new InputSlot<float>();

        [Input(Guid = "6bd240ad-0ff8-4282-a045-8e00d82cc017", MappedType = typeof(UseFSources))]
        public readonly InputSlot<int> ScaleFactor = new InputSlot<int>();

        [Input(Guid = "5247aeb2-7e69-4ff3-b432-77319f9b2467", MappedType = typeof(UseFSources))]
        public readonly InputSlot<int> SetF1To = new InputSlot<int>();

        [Input(Guid = "62130a78-6fe7-4146-89da-e536eb81501a", MappedType = typeof(UseFSources))]
        public readonly InputSlot<int> SetF2To = new InputSlot<int>();

        [Input(Guid = "0acbe3d0-53fa-47eb-b12e-453f1b61af33")]
        public readonly InputSlot<bool> ApplyPointScale = new InputSlot<bool>();

        [Input(Guid = "f15a003c-7969-4505-b598-6c6c4b5a3bbe")]
        public readonly InputSlot<bool> ApplyOrientation = new InputSlot<bool>();

        [Input(Guid = "6026d26d-b958-4508-b543-92fbdf8950d6")]
        public readonly InputSlot<bool> AddSeparators = new InputSlot<bool>();

        [Input(Guid = "f71ddebe-1f2c-47d0-ba39-eb5c4693e909")]
        public readonly InputSlot<bool> ApplyTargetScaleW = new InputSlot<bool>();

        [Input(Guid = "f582aa39-f5e0-46ad-89ae-6f29ab60d3e6")]
        public readonly InputSlot<bool> MultiplyTargetW = new InputSlot<bool>();


    private enum ConnectionModes
    {
        Linear,
        Interwoven,
    }
    
    private enum UseFModes
    {
        None,
        F1,
        F2,
        F1xF2
    }
    
    private enum UseFSources
    {
        None,
        Target_F1,
        Target_F2,
        Source_F1,
        Source_F2,
        Multiplied_F1,
        Multiplied_F2,
    }
}