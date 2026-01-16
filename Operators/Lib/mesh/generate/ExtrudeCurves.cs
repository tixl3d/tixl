namespace Lib.mesh.generate;

[Guid("816336a8-e214-4d2c-b8f9-05b1aa3ff2e2")]
internal sealed class ExtrudeCurves : Instance<ExtrudeCurves>
{

    [Output(Guid = "79ba19e0-13c3-40c7-8e0a-f190b03e95b0")]
    public readonly Slot<MeshBuffers> Output2 = new();

        [Input(Guid = "4d31be7a-3011-4fdf-9c63-425387b9bbfc")]
        public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> RailPoints = new InputSlot<T3.Core.DataTypes.BufferWithViews>();

        [Input(Guid = "5e2ada8d-10fa-419d-a377-0b504437fd72")]
        public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> ProfilePoints = new InputSlot<T3.Core.DataTypes.BufferWithViews>();

        [Input(Guid = "332e7d07-f6a2-4f58-8fe4-d2b368a63f4a")]
        public readonly InputSlot<float> Width = new InputSlot<float>();

        [Input(Guid = "13139afa-e01c-4cdd-abeb-8f33095a0f79", MappedType = typeof(FModes))]
        public readonly InputSlot<int> ScaleFactor = new InputSlot<int>();

        [Input(Guid = "7a2eff05-ab49-42ab-816c-86937f0ebbaf")]
        public readonly InputSlot<bool> UseExtend = new InputSlot<bool>();

        [Input(Guid = "e4591b4f-b2e7-496d-8e02-e574a0432737")]
        public readonly InputSlot<bool> UVsDirection = new InputSlot<bool>();

        [Input(Guid = "7c24f499-8021-4c67-9790-5cc7efb83287")]
        public readonly InputSlot<bool> UseWAsWidth = new InputSlot<bool>();


    private enum SampleModes
    {
        StartEnd,
        StartLength,
    }
    
    private enum FModes
    {
        None,
        RailPoint_F1,
        RailPoint_F2,
    }
}