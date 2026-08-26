namespace Lib.render.shading;

[Guid("facb7925-176a-4eae-bedc-cdbf532ff6ff")]
internal sealed class SetShadow : Instance<SetShadow>
{
    [Output(Guid = "a0a1b038-8637-45af-89b5-dcef99f872f7")]
    public readonly Slot<Command> Output = new Slot<Command>();

        [Input(Guid = "be6dc055-d4c8-4c75-a084-12c22a268034")]
        public readonly InputSlot<T3.Core.DataTypes.Command> Command = new InputSlot<T3.Core.DataTypes.Command>();

        [Input(Guid = "6196074f-4770-4e81-812a-012fcaab207b")]
        public readonly InputSlot<System.Numerics.Vector3> LightPosition = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "76a6a0e4-168d-4172-9946-e843df9d0ca0")]
        public readonly InputSlot<System.Numerics.Vector3> LightTarget = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "3ee1e726-b5b8-4479-92cc-1a46886ddfd2")]
        public readonly InputSlot<int> Resolution = new InputSlot<int>();

        [Input(Guid = "5fb309a1-f164-4fe0-95e2-3bfc37823f78")]
        public readonly InputSlot<System.Numerics.Vector2> DepthRange = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "bbec0f5e-b6ec-44b2-97ec-403419161b3a")]
        public readonly InputSlot<bool> ShowDebug = new InputSlot<bool>();

}