namespace Lib.image.fx.blur;

[Guid("1112d3ea-fef0-4d7c-a265-a067030256a1")]
internal sealed class FastBlur :Instance<FastBlur>{

        [Output(Guid = "e0a77e1e-d60f-4e37-987d-80fba2468497")]
        public readonly Slot<T3.Core.DataTypes.Texture2D> Result = new Slot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "c1a630e8-2d0b-412d-b2c2-c26e79befae2")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> Image = new InputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "01d8a4a8-56ca-4e6d-a6c9-8092e4153963")]
        public readonly InputSlot<int> MaxLevels = new InputSlot<int>();

}