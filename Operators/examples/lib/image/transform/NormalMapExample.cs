using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Examples.Lib.image.transform{
    [Guid("0c7a44a2-93ef-4e7e-8a82-a2ff4d73ac2a")]
    internal sealed class NormalMapExample : Instance<NormalMapExample>
    {
        [Output(Guid = "99558a50-6acd-4078-a12d-1df643a80696")]
        public readonly Slot<Texture2D> Output = new Slot<Texture2D>();


    }
}

