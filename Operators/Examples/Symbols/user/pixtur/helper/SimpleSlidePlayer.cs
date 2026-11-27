using System;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Examples.user.pixtur.helper{
    [Guid("494c51b3-f2f0-4549-9daa-09b9142fb014")]
    internal sealed class SimpleSlidePlayer : Instance<SimpleSlidePlayer>
    {

        [Input(Guid = "e8f30c1d-3ad5-4668-b3c1-a3c50b2b38d3")]
        public readonly InputSlot<string> FolderWithImages = new InputSlot<string>();

        [Output(Guid = "c11a7c5e-5bff-4354-b5be-c21dd3735d5e")]
        public readonly Slot<T3.Core.DataTypes.Texture2D> Output = new Slot<T3.Core.DataTypes.Texture2D>();

    }
}

