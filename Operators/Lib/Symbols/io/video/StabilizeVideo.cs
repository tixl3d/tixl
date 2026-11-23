using System;
using System;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using System.Runtime.InteropServices;

namespace Lib.io.video{
    [Guid("07837c27-0ab3-496b-92aa-05e0836106c4")]
    internal sealed class StabilizeVideo : Instance<StabilizeVideo>
    {
        [Output(Guid = "2722fdfd-d710-4b8e-86fd-87997d4513cb")]
        public readonly Slot<Command> Output = new Slot<Command>();

        [Input(Guid = "9a51b4c2-5437-4294-9549-21e33949fdb7")]
        public readonly InputSlot<string> Path = new InputSlot<string>();

        [Input(Guid = "f2d20773-e607-4b99-b250-6c1f3a5b2463")]
        public readonly InputSlot<int> ReferenceFrame = new InputSlot<int>();

        [Input(Guid = "876699f4-b588-4d3a-9c4e-6f08664f98f6")]
        public readonly InputSlot<bool> Reset = new InputSlot<bool>();

        [Input(Guid = "67896b9c-a1a4-4fa8-9246-fa1451024c59")]
        public readonly InputSlot<int> PoolSize = new InputSlot<int>();

        [Input(Guid = "36617d0a-4a51-469c-b4a7-3170db49b9b9")]
        public readonly InputSlot<System.Numerics.Vector2> FocusPoint = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "6cb36113-9240-402a-bd1d-9901c53e77a5")]
        public readonly InputSlot<float> FocusRadius = new InputSlot<float>();

        [Input(Guid = "23cc6760-c664-4c0c-8789-7d6067f50267")]
        public readonly InputSlot<bool> AllowRotation = new InputSlot<bool>();

        [Input(Guid = "4beaba83-65e7-47ab-b56e-7390956e66da")]
        public readonly InputSlot<bool> AllowScale = new InputSlot<bool>();

        [Input(Guid = "ee9afc76-b55f-4bb2-9538-e459f6dde06d")]
        public readonly InputSlot<T3.Core.Operator.GizmoVisibility> Visibility = new InputSlot<T3.Core.Operator.GizmoVisibility>();

        [Input(Guid = "095a7e31-b427-41b8-805d-e4c54e73d0b6")]
        public readonly InputSlot<System.Numerics.Vector4> Color = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "1a03ffa2-053c-4c81-b9ac-ae3b65189c3f")]
        public readonly InputSlot<bool> ShowGrid = new InputSlot<bool>();

        [Input(Guid = "439ae155-4072-490f-be80-b850f654193f")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> TrackingMask = new InputSlot<T3.Core.DataTypes.Texture2D>();

    }
}

