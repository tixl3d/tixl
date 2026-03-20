namespace Lib.point._experimental;

[Guid("b0981921-8e3d-4010-8b67-d7dcecca7758")]
internal sealed class NumberLinePoints :Instance<NumberLinePoints>{

    [Output(Guid = "55d34be3-784c-4f77-afbb-d75b036fcdbd")]
    public readonly Slot<BufferWithViews> OutBuffer = new();

        [Input(Guid = "c973d9cf-525e-4910-b266-ecebd7df49af")]
        public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> GTargets = new InputSlot<T3.Core.DataTypes.BufferWithViews>();

        [Input(Guid = "d5bb30ae-6368-464b-b7db-ee30b9c3c394")]
        public readonly InputSlot<float> Scale = new InputSlot<float>();

        [Input(Guid = "65d4a758-fc55-4b8f-9e87-7fa5a1a00cb8")]
        public readonly InputSlot<System.Collections.Generic.List<int>> NumericValues = new InputSlot<System.Collections.Generic.List<int>>();

        [Input(Guid = "bc3dee4d-8f01-4cd3-8a2b-7dc30966303c")]
        public readonly InputSlot<int> MaxDigitCount = new InputSlot<int>();

        [Input(Guid = "7b55d494-26bc-4fa5-9476-b4c12c503972")]
        public readonly InputSlot<float> Spacing = new InputSlot<float>();

        [Input(Guid = "eb4007f8-247d-487a-bad7-f136eba821ea", MappedType = typeof(NumberModes))]
        public readonly InputSlot<int> NumberMode = new InputSlot<int>();

        [Input(Guid = "56482ceb-ae0e-4d32-a6a4-2568a166a2d9")]
        public readonly InputSlot<int> FloatPrecision = new InputSlot<int>();

        [Input(Guid = "b5b3ce43-8e27-4839-86ed-a1d80c14a5c5")]
        public readonly InputSlot<int> Increment = new InputSlot<int>();

        [Input(Guid = "d348dd16-e165-470c-89c2-83c73cb792d1")]
        public readonly InputSlot<float> LineWidth = new InputSlot<float>();


    private enum NumberModes
    {
        UseValueList,
        UseFx1,
        UseFx2,
    }
}