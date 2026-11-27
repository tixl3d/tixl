using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Examples.Lib.geometry.modify{
    [Guid("15d86cab-dafe-4741-bd0a-519a2f7c1eb5")]
    internal sealed class VoronoiFractureExample : Instance<VoronoiFractureExample>
    {
        [Output(Guid = "21733197-e845-412f-96b9-e7871d2a8f15")]
        public readonly Slot<Texture2D> Output = new Slot<Texture2D>();


    }
}

