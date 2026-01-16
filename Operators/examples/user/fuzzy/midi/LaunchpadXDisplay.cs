using T3.Core.DataTypes;
using System.Collections.Generic;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Examples.user.fuzzy.midi{
    [Guid("4369c0d2-2722-453d-a47a-aa9cc4f86d4b")]
    internal sealed class LaunchpadXDisplay : Instance<LaunchpadXDisplay>
    {

        [Output(Guid = "45d94643-0028-4b4e-9354-60481fa5ac37")]
        public readonly Slot<T3.Core.DataTypes.Command> OutputCmd = new Slot<T3.Core.DataTypes.Command>();


        [Input(Guid = "6147621b-9224-46da-8cf1-d7956da53b5e")]
        public readonly InputSlot<Texture2D> Texture = new InputSlot<Texture2D>();

        [Input(Guid = "26b29682-bcb1-47ff-a487-3db0b56c3274")]
        public readonly InputSlot<string> MidiDevice = new InputSlot<string>();

    }
}

