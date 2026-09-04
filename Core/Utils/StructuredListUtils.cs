using System.Reflection;
using T3.Core.DataTypes;
using T3.Core.Logging;
using T3.Core.Operator;
using T3.Core.Operator.Slots;

namespace T3.Core.Utils;

// Todo: can this be removed?
public class StructuredListUtils
{
    public static T GetValueOfFieldWithType<T>(EvaluationContext context, InputSlot<StructuredList> dataListInput, InputSlot<int> itemIndex, InputSlot<int> FieldIndex,
                                               InputSlot<string> orFieldName, ref FieldInfo fieldRef)
    {
        var list = dataListInput.GetValue(context);
        if (list == null || list.NumElements == 0)
            return default;

        var index = itemIndex.GetValue(context) % list.NumElements;

        var item = list[index];
        var fieldInfos = list.Type.GetFields();

        var needToUpdateIndex = FieldIndex.DirtyFlag.IsDirty || orFieldName.DirtyFlag.IsDirty;
        if (needToUpdateIndex)
        {
            var requestedFieldIndex = FieldIndex.GetValue(context);
            var requestedFieldName = orFieldName.GetValue(context);

            fieldRef = null;
            var fieldIndex = 0;
            foreach (var field in fieldInfos)
            {
                if (fieldIndex == requestedFieldIndex || field.Name == requestedFieldName)
                {
                    if (field.GetValue(item) is T f)
                    {
                        fieldRef = field;
                        return f;
                    }
                }

                fieldIndex++;
            }

            Log.Warning($"There is not Float field for index {requestedFieldIndex} or name '{requestedFieldName}' in type {list.Type.Name}");
        }
        else if (fieldRef?.GetValue(item) is T f)
        {
            return f;
        }

        return default;
    }
        
    public static T GetIteratedValueOfFieldWithType<T>(EvaluationContext context,  
                                                       InputSlot<string> orFieldName, ref FieldInfo fieldRef)
    {
        var list = context.IteratedList;
        if (list == null || list.NumElements == 0)
            return default;
            
            
        var index = context.IteratedListIndex % list.NumElements;   // TODO: Clarify if default Modulo is a good choice
        var item = list[index];
        var fieldInfos = list.Type.GetFields();
        if (orFieldName.DirtyFlag.IsDirty)
        {
                
            var requestedFieldName = orFieldName.GetValue(context);

            fieldRef = null;
            foreach (var field in fieldInfos)
            {
                if (field.Name == requestedFieldName)
                {
                    if (field.GetValue(item) is T f)
                    {
                        fieldRef = field;
                        return f;
                    }
                }
            }

            Log.Warning($"There is not Float field for  name '{requestedFieldName}' in type {list.Type.Name}");
        }
        else if (fieldRef?.GetValue(item) is T f)
        {
            return f;
        }

        return default;
    }        
}