using Newtonsoft.Json;

namespace Lib.numbers.data._obsolete;

[Guid("d5607e3b-15e8-402c-8d54-b29e40415ab0")]
internal sealed class ExportPointList : Instance<ExportPointList>
{
    [Output(Guid = "ba3d861e-3e22-4cea-9070-b7f53059cf87")]
    public readonly Slot<StructuredList> Result = new();

        
    public ExportPointList()
    {
        Result.UpdateAction += Update;
            
            
    }

    //private bool _triggerSave;

    private void Update(EvaluationContext context)
    {
        //var saveTriggered = MathUtils.WasTriggered(TriggerSave.GetValue(context), ref _triggerSave);
        var filepath = FilePath.GetValue(context);
        var inputListValue = InputList.GetValue(context);
        Log.Debug("Export points to " + filepath, this);
        InputList.TypedDefaultValue.Value = inputListValue;
        var filterDoubleNan = FilterDoubleNaN.GetValue(context);

        if (TriggerSave.GetValue(context))
        {
            if (inputListValue is not StructuredList<Point> pointList)
            {
                Log.Warning("Structured List is not a point list");
                return;
            }

            try
            {
                if (filterDoubleNan)
                {
                    var lastItemWasNan = false;
                    // Count points
                    var doubleNanCount = 0;
                    foreach (var p in pointList.TypedElements)
                    {
                        if (!float.IsNaN(p.F1))
                        {
                            lastItemWasNan = false;
                            continue;
                        }

                        if (lastItemWasNan)
                            doubleNanCount++;

                        lastItemWasNan = true;
                    }

                    var newCount = pointList.NumElements - doubleNanCount;
                    var filterPointList = new StructuredList<Point>(newCount);
                        
                    // Count points
                    var filteredIndex = 0;
                    foreach (var p in pointList.TypedElements)
                    {
                        if (!float.IsNaN(p.F1))
                        {
                            filterPointList[filteredIndex] = p;
                            filteredIndex ++;
                            lastItemWasNan = false;
                        }
                        else
                        {
                            if (!lastItemWasNan)
                            {
                                filterPointList[filteredIndex] = p;
                                filteredIndex ++;
                            }
                            lastItemWasNan = true;
                        }
                    }

                    pointList = filterPointList;
                }
                    
                using var sw = new StreamWriter(filepath);
                using var writer = new JsonTextWriter(sw) { Formatting = Formatting.Indented };
                pointList.Write(writer);
            }
            catch (Exception e)
            {
                Log.Warning("Failed to export: " + e.Message);
            }
        }
                
        Result.Value = inputListValue;
    }
        
        
    [Input(Guid = "B0DCEB47-4EE5-481F-A310-173C336F4FBE")]
    public readonly InputSlot<bool> TriggerSave = new ();
        
    [Input(Guid = "043E9707-98FE-420C-A546-292459C2FCCA")]
    public readonly InputSlot<string> FilePath = new ();
        
    [Input(Guid = "B2158C69-E671-4110-B398-59C323359D2D")]
    public readonly InputSlot<bool> FilterDoubleNaN = new ();


    [Input(Guid = "b3d57d74-ac47-4287-b42a-d85e64501eb5")]
    public readonly InputSlot<StructuredList> InputList = new(new StructuredList<Point>(15));
        
}