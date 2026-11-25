using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Examples.Lib.point.draw{
    [Guid("9b56b267-2b83-495d-8f31-90402b19ecec")]
    internal sealed class DrawTubesExample : Instance<DrawTubesExample>
    {
        [Output(Guid = "6dc7d831-5696-4128-9062-b5641f444b33")]
        public readonly Slot<Texture2D> Output = new Slot<Texture2D>();


    }
}

