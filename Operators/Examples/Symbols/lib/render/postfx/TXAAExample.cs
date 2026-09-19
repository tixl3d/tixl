using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Examples.Lib.render.postfx{
    [Guid("6c179e4c-a63d-4866-9dd1-2453d16522bd")]
    internal sealed class TXAAExample : Instance<TXAAExample>
    {
        [Output(Guid = "f90b4b32-eb60-4c0a-b349-5f95ed8500f4")]
        public readonly Slot<Texture2D> TextureOutput = new Slot<Texture2D>();


    }
}

