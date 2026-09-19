using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Examples.user.fuzzy.midi{
    [Guid("55551bc1-1bf9-47d8-b25b-33c3596d21c4")]
    internal sealed class LaunchpadXExample : Instance<LaunchpadXExample>
    {

        [Output(Guid = "aeeff0ab-3338-4901-9959-374056721ab7")]
        public readonly Slot<T3.Core.DataTypes.Texture2D> Output = new Slot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "3a7ecd51-eaf2-4eb9-a672-dc3de839f496")]
        public readonly InputSlot<bool> InitTrigger = new InputSlot<bool>();

    }
}

