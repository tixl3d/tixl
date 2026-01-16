using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Examples.Lib.field.adjust{
    [Guid("8ec3372c-c5de-4c4d-abe1-cccc3b8dc472")]
    internal sealed class SpatialDisplaceSDFExample : Instance<SpatialDisplaceSDFExample>
    {
        [Output(Guid = "adfbfb97-7862-4667-b415-73d92f6540d4")]
        public readonly Slot<Texture2D> ColorBuffer = new Slot<Texture2D>();


    }
}

