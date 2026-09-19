using T3.Core.DataTypes;
using System.Collections.Generic;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Examples.user.fuzzy.midi{
    [Guid("8937e098-e136-475b-82ec-17c3e65a379f")]
    internal sealed class LaunchPadXFader4 : Instance<LaunchPadXFader4>
    {

        [Output(Guid = "c7316411-707a-4769-8d3e-77ad3628d126")]
        public readonly Slot<T3.Core.DataTypes.Command> OutputCmd = new Slot<T3.Core.DataTypes.Command>();

        [Output(Guid = "7d6d7c11-cadb-4d7e-91e2-5ebe7dd19ea6")]
        public readonly Slot<int> IntOut = new Slot<int>();

        [Output(Guid = "7326a5a9-7584-401b-9520-5453e9a3b911")]
        public readonly Slot<float> FloatOut = new Slot<float>();

        [Input(Guid = "5bdee277-3ea4-4d17-9465-c59f6d50c0e6")]
        public readonly InputSlot<string> MidiDevice = new InputSlot<string>();

        [Input(Guid = "2672c0fd-5db5-451a-a02c-3af27b0262ab")]
        public readonly InputSlot<int> X = new InputSlot<int>();

        [Input(Guid = "6fc44168-1dd7-4632-a0aa-7d18fd71172f")]
        public readonly InputSlot<int> Y = new InputSlot<int>();

        [Input(Guid = "37705008-bf6b-4f80-991c-a742ef9a2bff")]
        public readonly InputSlot<System.Numerics.Vector4> Color = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "7d395428-6562-4c76-a252-395bcb170d50")]
        public readonly InputSlot<bool> Init = new InputSlot<bool>();

    }
}

