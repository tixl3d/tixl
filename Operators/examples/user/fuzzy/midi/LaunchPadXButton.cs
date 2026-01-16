using T3.Core.DataTypes;
using System.Collections.Generic;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Examples.user.fuzzy.midi{
    [Guid("7d1f025c-89be-4ae3-9693-6da677a0ae2b")]
    internal sealed class LaunchPadXButton : Instance<LaunchPadXButton>
    {

        [Output(Guid = "d9333cef-5073-4126-b3a0-3192c880d297")]
        public readonly Slot<T3.Core.DataTypes.Command> OutputCmd = new Slot<T3.Core.DataTypes.Command>();

        [Output(Guid = "458a1bab-dce5-4262-9e35-2f44248f4b4c")]
        public readonly Slot<bool> BoolOut = new Slot<bool>();

        [Input(Guid = "27fce040-5e4b-4e39-9e05-21f0b5bee340")]
        public readonly InputSlot<string> MidiDevice = new InputSlot<string>();

        [Input(Guid = "4e6000e1-35bd-487b-a5c1-fd0a522d1889")]
        public readonly InputSlot<int> X = new InputSlot<int>();

        [Input(Guid = "5057eff8-6209-4225-8daf-af1447c43e88")]
        public readonly InputSlot<int> Y = new InputSlot<int>();

        [Input(Guid = "6ae6bdf6-0a90-401c-aa74-6cffed541ef2")]
        public readonly InputSlot<System.Numerics.Vector4> Color = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "ae6b45ac-4fb7-46a4-9921-f1462ad25ae2")]
        public readonly InputSlot<bool> Init = new InputSlot<bool>();

        [Input(Guid = "cc43d859-0211-470a-a74a-e9f40ed037d3")]
        public readonly InputSlot<bool> State = new InputSlot<bool>();

        [Input(Guid = "e21eb02c-5342-4972-a390-6fb11f6a5a32")]
        public readonly InputSlot<bool> FromState = new InputSlot<bool>();

        [Input(Guid = "dac587a2-aa4d-40d4-98ca-5924c6dea03f")]
        public readonly InputSlot<bool> Toggle = new InputSlot<bool>();

    }
}

