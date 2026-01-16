using System;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Lib.flow.skillQuest{
    [Guid("64b2f2d5-0261-4f22-b30f-1f3a1421fe2e")]
    internal sealed class _ValueQuizGraph : Instance<_ValueQuizGraph>
    {
        [Output(Guid = "88844b1e-255e-4783-a906-239fefe38afd")]
        public readonly Slot<Texture2D> ColorBuffer = new Slot<Texture2D>();

        [Output(Guid = "a1a8083d-034f-4520-9a65-1f11f16cf93f")]
        public readonly Slot<System.Collections.Generic.List<float>> Values = new Slot<System.Collections.Generic.List<float>>();


        [Input(Guid = "afcdab0a-b859-4137-82de-3fc20f306d57")]
        public readonly InputSlot<float> Value = new InputSlot<float>();

        [Input(Guid = "7148d3f0-7135-40c8-a204-d52a043d494d")]
        public readonly InputSlot<System.Numerics.Vector2> DisplayValueRange = new InputSlot<System.Numerics.Vector2>();

    }
}

