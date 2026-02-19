using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Lib.render.postfx{
    [Guid("a2100b4a-be2b-474f-ab53-07198580c995")]
    internal sealed class ProjectedLight : Instance<ProjectedLight>
    {
        [Output(Guid = "6737d18c-ab47-4fc5-9ccd-37634485bb01")]
        public readonly Slot<Texture2D> Output = new Slot<Texture2D>();


    }
}

