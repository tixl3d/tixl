using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Examples.Lib.render.camera{
    [Guid("28712e30-6ee2-4118-a35a-21834ef266ee")]
    internal sealed class ActionCameraExample : Instance<ActionCameraExample>
    {
        [Output(Guid = "71e9855d-6d38-4df3-bbe4-7cd5c8154e14")]
        public readonly Slot<Texture2D> ColorBuffer = new Slot<Texture2D>();


    }
}

