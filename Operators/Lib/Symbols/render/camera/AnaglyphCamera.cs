using T3.Core.DataTypes;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Lib.render.camera{
    [Guid("fe4fdf42-1e3e-4f62-994a-a3e87d02911c")]
    internal sealed class AnaglyphCamera : Instance<AnaglyphCamera>
    {
        [Output(Guid = "e0baba58-d572-4027-a903-fcd06a9deecb")]
        public readonly Slot<Command> Output = new Slot<Command>();


        [Input(Guid = "bb6a5f7a-93fd-4972-badc-2e9e682e4958")]
        public readonly InputSlot<Command> Command = new InputSlot<Command>();

        [Input(Guid = "0dd39a9b-d99e-44ed-8242-0d232375c333")]
        public readonly InputSlot<float> EyeOffet = new InputSlot<float>();

        [Input(Guid = "9dd036ba-d890-4e46-8c98-ecf5201bdadc")]
        public readonly InputSlot<float> PlaneSplit = new InputSlot<float>();

        [Input(Guid = "11afcfc5-7013-43f6-a050-3183aba2cb5f")]
        public readonly InputSlot<Object> CamReference = new InputSlot<Object>();

    }
}

