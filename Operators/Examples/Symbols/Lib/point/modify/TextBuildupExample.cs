using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Examples.Lib.point.modify{
    [Guid("de222a06-2d8c-481d-9c19-667aaaa3b254")]
    internal sealed class TextBuildupExample : Instance<TextBuildupExample>
    {
        [Output(Guid = "c64d5027-9d99-45a4-a894-451980e96740")]
        public readonly Slot<Texture2D> Result = new Slot<Texture2D>();


    }
}

