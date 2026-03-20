using System;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Lib.render.camera.analyze{
    [Guid("d59ee7b2-8beb-470e-b75a-f73cc1916b1a")]
    internal sealed class VisualizeCamTrail : Instance<VisualizeCamTrail>
    {
        [Output(Guid = "612ee50f-5c53-4e04-93fe-bd72c6953a9f")]
        public readonly Slot<Command> Result = new Slot<Command>();


        [Input(Guid = "7fd1c5f7-de89-46ed-b8ba-d14c2352848f")]
        public readonly InputSlot<Object> CamReference = new InputSlot<Object>();

        [Input(Guid = "8fceaa45-96e1-446c-9e5d-d1ea23380aa1")]
        public readonly InputSlot<int> Steps = new InputSlot<int>();

        [Input(Guid = "9ca00da6-18b7-449e-be38-a42ef20f6c0a")]
        public readonly InputSlot<float> TimeRange = new InputSlot<float>();

    }
}

