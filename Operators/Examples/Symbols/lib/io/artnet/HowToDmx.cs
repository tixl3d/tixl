using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Examples.Lib.io.artnet{
    [Guid("0ea8b502-6489-46df-86ed-82d882f67817")]
    internal sealed class HowToDmx : Instance<HowToDmx>
    {

        [Input(Guid = "84cc00e5-fb26-4a19-be23-0450c5faf286")]
        public readonly InputSlot<int> Selector = new InputSlot<int>();

        [Output(Guid = "8fffe02b-c9d4-46ad-bcda-4a68c288d582")]
        public readonly Slot<T3.Core.DataTypes.Command> Out = new Slot<T3.Core.DataTypes.Command>();

    }
}

