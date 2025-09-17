using T3.Core.Utils;
using BreedList = T3.Core.DataTypes.StructuredList<Examples.user.pixtur.learning.cs._05_physarum._SpeciesDefinition.Breed>;

namespace Examples.user.pixtur.learning.cs._05_physarum;

[Guid("924b8cc0-5b4b-41d0-a71b-b26465683910")]
public class _SpeciesDefinition : Instance<_SpeciesDefinition>
{
    [Output(Guid = "55498833-FF69-489F-AFE6-D54150920C56")]
    public readonly Slot<StructuredList> OutBuffer = new();
        
    public _SpeciesDefinition()
    {
        OutBuffer.UpdateAction += Update;
        _slots = new List<IInputSlot>()
                     {
                         ComfortZones,
                         Emit,
                         SideAngle,
                         SideRadius,
                         FrontRadius,
                         BaseMovement,
                         BaseRotation,
                         MoveToComfort,
                         RotateToComfort,
                     };                   
    }


        
    private void Update(EvaluationContext context)
    {
        if (IsAnyInputDirty() || !_initialized)
        {
            _breeds.TypedElements[0].ComfortZones = ComfortZones.GetValue(context);
            _breeds.TypedElements[0].Emit = Emit.GetValue(context);
            _breeds.TypedElements[0].SideAngle = SideAngle.GetValue(context) * MathUtils.ToRad;
            _breeds.TypedElements[0].SideRadius = SideRadius.GetValue(context);
            _breeds.TypedElements[0].FrontRadius = FrontRadius.GetValue(context);
            _breeds.TypedElements[0].BaseMovement = BaseMovement.GetValue(context);
            _breeds.TypedElements[0].BaseRotation = BaseRotation.GetValue(context) * MathUtils.ToRad;
            _breeds.TypedElements[0].MoveToComfort = MoveToComfort.GetValue(context);
            _breeds.TypedElements[0].RotateToComfort = RotateToComfort.GetValue(context) * MathUtils.ToRad;
            _initialized = true;
        }
        OutBuffer.Value = _breeds;
    }
        
    [StructLayout(LayoutKind.Explicit, Size = 16 * 4)]
    public struct Breed
    {
        [FieldOffset(0 * 4)]
        public Vector4 ComfortZones;
        
        [FieldOffset(4 * 4)]
        public Vector4 Emit;
        
        [FieldOffset(8 * 4)]
        public float SideAngle;
        
        [FieldOffset(9 * 4)]
        public float SideRadius;
        
        [FieldOffset(10 * 4)]
        public float FrontRadius;
        
        [FieldOffset(11 * 4)]
        public float BaseMovement;
        
        [FieldOffset(12 * 4)]
        public float BaseRotation;
        
        [FieldOffset(13 * 4)]
        public float MoveToComfort;
        
        [FieldOffset(14 * 4)]
        public float RotateToComfort;
        
        [FieldOffset(15 * 4)]
        public float _padding;
    }

    private bool IsAnyInputDirty()
    {
        foreach (var i in _slots)
        {
            if (i.DirtyFlag.IsDirty)
                return true;
        }

        return false;
    }
        
        
    private BreedList _breeds = new(1);
    private readonly List<IInputSlot> _slots;
    private bool _initialized;
        
        
    [Input(Guid = "8C4E188C-18AB-4F69-A60A-14A8E5A12F91")]
    public readonly InputSlot<System.Numerics.Vector4> ComfortZones = new();
        
    [Input(Guid = "9A4D8846-6B46-4A4B-A8D3-97F3F9EAF8DB")]
    public readonly InputSlot<Vector4> Emit = new();
        
    [Input(Guid = "4DD19C0B-10C1-43FA-A3E2-970B4F9C6162")]
    public readonly InputSlot<float> SideAngle = new();
        
    [Input(Guid = "11BA5BBC-4873-489F-85B7-35080F0988CF")]
    public readonly InputSlot<float> SideRadius = new();
        
    [Input(Guid = "E95C5F4D-DF12-42F6-A879-8E26540B03AC")]
    public readonly InputSlot<float> FrontRadius = new();
        
    [Input(Guid = "6EB81DC2-88B5-4BA7-9F82-FE2389DC2926")]
    public readonly InputSlot<float> BaseMovement = new();
        
    [Input(Guid = "E9FD2C91-7CEE-481E-933B-A40A27DA15DC")]
    public readonly InputSlot<float> BaseRotation = new();
        
    [Input(Guid = "211FD6EE-26A9-4E15-85BA-4A22E865545D")]
    public readonly InputSlot<float> MoveToComfort = new();
        
    [Input(Guid = "8367CBAD-6214-4167-855B-9F704BB46AC3")]
    public readonly InputSlot<float> RotateToComfort = new();
}