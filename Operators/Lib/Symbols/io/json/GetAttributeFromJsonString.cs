using System.Data;
using Newtonsoft.Json;

namespace Lib.io.json;

[Guid("43783ad9-72a0-4928-bb86-c89aae5c5d85")]
internal sealed class GetAttributeFromJsonString : Instance<GetAttributeFromJsonString>, IStatusProvider
{
    [Output(Guid = "6fd32e9f-4d75-4243-ad8e-314ff738f76d")]
    public readonly Slot<string> Result = new();
        
    [Output(Guid = "00F51B94-8AC0-4300-917A-EDC952726C5F")]
    public readonly Slot<List<string>> Columns = new();
        
    [Output(Guid = "C0F9495F-659B-4849-AB9F-A82376C59371")]
    public readonly Slot<int> RowCount = new();
        
    public GetAttributeFromJsonString()
    {
        Result.UpdateAction += Update;
        Columns.UpdateAction += Update;
        RowCount.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var columnName = ColumnName.GetValue(context);
        var rowIndex = RowIndex.GetValue(context);
            
        var json = JsonString.GetValue(context);
        try
        {
            _lastStatusError = string.Empty;
                
            var dt = JsonConvert.DeserializeObject<DataTable>(json);
            // Log.Debug("" + dt.Columns.Count);
            var columns = new List<string>();
            RowCount.Value = dt.Rows.Count;
                
            var matchingColumnIndex = -1;
            var index = 0;
            foreach (DataColumn c in dt.Columns)
            {
                if (c.ColumnName.Equals(columnName, StringComparison.InvariantCultureIgnoreCase))
                {
                    matchingColumnIndex = index;
                }
                index++;
                columns.Add(c.ColumnName);
            }

            if (matchingColumnIndex == -1)
            {
                _lastStatusError = $"Can't find column {columnName}";
            }
            else
            {
                if (dt.Rows.Count < rowIndex || rowIndex < 0)
                {
                    _lastStatusError = $"Row index {rowIndex} exceeds {dt.Rows.Count}.";
                }
                else
                {
                    var xxx = dt.Rows[rowIndex];
                    Result.Value= xxx[matchingColumnIndex].ToString();
                }
            }
                
            Columns.Value = columns;
        }
        catch (Exception e)
        {
            Log.Warning("Couldn't parse json to data table " + e.Message);
        }
    }

        
    [Input(Guid = "918d69a9-c29a-43fe-9961-c1dcde31e2ec")]
    public readonly InputSlot<string> JsonString = new();
        
    [Input(Guid = "DADD353A-2195-4B52-80D2-88288C55B0E4")]
    public readonly InputSlot<string> ColumnName = new();
        
    [Input(Guid = "D7820C58-22FD-492A-81FE-870AB1912B0A")]
    public readonly InputSlot<int> RowIndex = new();
        
    public IStatusProvider.StatusLevel GetStatusLevel()
    {
        return string.IsNullOrEmpty(_lastStatusError) ? IStatusProvider.StatusLevel.Success: IStatusProvider.StatusLevel.Warning;
    }

    public string GetStatusMessage()
    {
        return _lastStatusError;
    }
        
    private string _lastStatusError;
}