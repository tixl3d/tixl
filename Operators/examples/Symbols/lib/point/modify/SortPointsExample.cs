using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Examples.Lib.point.modify{
    [Guid("1e24d0ab-40f4-4791-a929-fb08584f516d")]
    internal sealed class SortPointsExample : Instance<SortPointsExample>
    {

        [Output(Guid = "8a7ca6b0-6993-4b07-acdb-e412287134f5")]
        public readonly Slot<T3.Core.DataTypes.Texture2D> ColorBuffer = new Slot<T3.Core.DataTypes.Texture2D>();

    }
}

