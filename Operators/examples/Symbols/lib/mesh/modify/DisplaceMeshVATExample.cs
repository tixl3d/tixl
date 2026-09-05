using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Examples.Lib.mesh.modify{
    [Guid("648cd0f0-94bf-4fde-9fb7-ba4ccf30301e")]
    internal sealed class DisplaceMeshVATExample : Instance<DisplaceMeshVATExample>
    {
        [Output(Guid = "32c5a001-7cc1-45d1-8b18-0805d106dfae")]
        public readonly Slot<Texture2D> ColorBuffer = new Slot<Texture2D>();


    }
}

