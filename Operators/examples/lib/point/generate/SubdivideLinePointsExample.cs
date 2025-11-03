using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Examples.Lib.point.generate{
    [Guid("2828fe15-4267-490c-9c12-6d6d3baf8ae9")]
    internal sealed class SubdivideLinePointsExample : Instance<SubdivideLinePointsExample>
    {
        [Output(Guid = "e4eb4bf7-8ffd-4105-b648-ef76164f6ccf")]
        public readonly Slot<Texture2D> ColorBuffer = new Slot<Texture2D>();


    }
}

