using System.Reflection;
using T3.Core.Utils;

namespace Lib.numbers.data.utils;

[Guid("37794826-a099-4af3-90f4-1e49092a09e1")]
internal sealed class GetListItemAttribute : Instance<GetListItemAttribute>
{
    [Output(Guid = "0C83599F-1E0F-4BBF-B662-56B4CA5099B0", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> Result = new();

    public GetListItemAttribute()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        Result.Value = StructuredListUtils.GetValueOfFieldWithType<float>(context, DataList, ItemIndex, FieldIndex, OrFieldName, ref _field);
    }

    private FieldInfo _field;

    [Input(Guid = "9CF1AE77-1E80-443A-BB07-F545C3D2E71D")]
    public readonly InputSlot<StructuredList> DataList = new();

    [Input(Guid = "09F5501D-5F51-4406-B0CD-4C3E572A376F")]
    public readonly InputSlot<int> ItemIndex = new();

    [Input(Guid = "6A988C18-DFE1-4F52-9019-ED97BD61A6E8")]
    public readonly InputSlot<int> FieldIndex = new();

    [Input(Guid = "C6B58AB1-7311-4CF9-B013-C021683FA159")]
    public readonly InputSlot<string> OrFieldName = new();
}