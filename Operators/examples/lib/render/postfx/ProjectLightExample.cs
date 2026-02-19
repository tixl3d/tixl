using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Examples.Lib.render.postfx{
    [Guid("45b244bb-2a15-4fd7-96f6-3fb1eccfbc09")]
    internal sealed class ProjectLightExample : Instance<ProjectLightExample>
    {
        [Output(Guid = "d994974c-d4bd-45c5-9461-54db087cd9dc")]
        public readonly Slot<Texture2D> Output = new Slot<Texture2D>();


    }
}

