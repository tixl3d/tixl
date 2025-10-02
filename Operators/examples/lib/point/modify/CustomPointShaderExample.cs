using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Examples.Lib.point.modify{
    [Guid("d8feac82-1a5d-4c6f-bd1f-67ed7fc28193")]
    internal sealed class CustomPointShaderExample : Instance<CustomPointShaderExample>
    {
        [Output(Guid = "964f01d9-769d-4a96-a566-7f7c6cbb4ad2")]
        public readonly Slot<Texture2D> ColorBuffer = new Slot<Texture2D>();


    }
}

