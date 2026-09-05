using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Examples.Lib.point.transform{
    [Guid("57c7842e-c14a-4e27-aa32-4808434ec71b")]
    internal sealed class IkChainExample : Instance<IkChainExample>
    {
        [Output(Guid = "52b11d65-28d7-4613-81c5-499551b7962d")]
        public readonly Slot<Texture2D> ColorBuffer = new Slot<Texture2D>();


    }
}

