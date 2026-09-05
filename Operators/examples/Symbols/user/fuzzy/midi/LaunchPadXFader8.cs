using T3.Core.DataTypes;
using System.Collections.Generic;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Examples.user.fuzzy.midi{
    [Guid("c4d3e590-1780-4419-b946-7b3fc7d718aa")]
    internal sealed class LaunchPadXFader8 : Instance<LaunchPadXFader8>
    {

        [Output(Guid = "9ef54700-8366-45a5-b148-d070b9fc2bbe")]
        public readonly Slot<T3.Core.DataTypes.Command> OutputCmd = new Slot<T3.Core.DataTypes.Command>();

        [Output(Guid = "61786e6d-406f-41d1-aa26-da56c56708ae")]
        public readonly Slot<int> IntOut = new Slot<int>();

        [Output(Guid = "89983c00-7219-456c-8bbd-f1a31bd47dfa")]
        public readonly Slot<float> FloatOut = new Slot<float>();

        [Input(Guid = "7e8a8f28-a114-439b-b4f6-b8a4a7be2d3f")]
        public readonly InputSlot<string> MidiDevice = new InputSlot<string>();

        [Input(Guid = "2754a005-4763-4b74-8580-2eb684117e8c")]
        public readonly InputSlot<int> X = new InputSlot<int>();

        [Input(Guid = "427c03d6-b3b0-4318-99e4-139e12484e8e")]
        public readonly InputSlot<int> Y = new InputSlot<int>();

        [Input(Guid = "d4403c6f-cc7c-446b-917d-b7d1e635fa7a")]
        public readonly InputSlot<System.Numerics.Vector4> Color = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "9a614d16-24b0-4a8d-8af0-84456b627ec8")]
        public readonly InputSlot<bool> Init = new InputSlot<bool>();

    }
}

