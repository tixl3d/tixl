using System.Reflection;
using T3.Core.Utils;

namespace Lib.numbers.data._obsolete;

[Guid("d86e9585-d233-455a-9059-fa93debfed01")]
internal sealed class GetIteratedVec3 : Instance<GetIteratedVec3>
{
    [Output(Guid = "E9839288-05DB-41E7-8326-3BE4F65F2410", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Vector3> Result = new();

    public GetIteratedVec3()
    {
        Result.UpdateAction += Update;
        Result.DirtyFlag.Trigger |= DirtyFlagTrigger.Animated;
    }

    private void Update(EvaluationContext context)
    {
        Result.Value = StructuredListUtils.GetIteratedValueOfFieldWithType<Vector3>(context, FieldName, ref _field);
    }

    private FieldInfo _field;


    [Input(Guid = "18dc4a34-f0c1-4763-be36-71b59533916a")]
    public readonly InputSlot<string> FieldName = new();
}