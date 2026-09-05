using System.Text.RegularExpressions;

namespace Lib.@string.search;

[Guid("b7910fc6-c3b2-4daf-93cd-010dcfe22a57")]
internal sealed class SearchAndReplace : Instance<SearchAndReplace>
{
    [Output(Guid = "15672e8f-c483-432e-8ced-f2bd18c1be67")]
    public readonly Slot<string> Result = new();

    public SearchAndReplace()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var useRegex = UseRegex.GetValue(context);
        var content = OriginalString.GetValue(context);
        var replacement = Replace.GetValue(context)?.Replace("\\n","\n");
        var pattern = SearchPattern.GetValue(context);
        if (string.IsNullOrEmpty(content) 
            || string.IsNullOrEmpty(replacement)
            || string.IsNullOrEmpty(pattern))
        {
            Result.Value = content?? string.Empty;
            return;
        }

        if (useRegex)
        {
            try
            {
                Result.Value= Regex.Replace(content, pattern, replacement, RegexOptions.Multiline| RegexOptions.Singleline);
            }
            catch (Exception)
            {
                Log.Error($"'{pattern}' is an incorrect search pattern", this);
            }
        }
        else
        {
            Result.Value = content.Replace(pattern, replacement, StringComparison.Ordinal);
        }
    }
        
    [Input(Guid = "3ca66cbd-a16a-479c-b858-84732e5023ad")]
    public readonly InputSlot<string> OriginalString = new();
        
    [Input(Guid = "4FE3F641-1C36-4970-BE71-DAFB5632FB53")]
    public readonly InputSlot<string> SearchPattern = new();
        
    [Input(Guid = "DE8297AE-C7D8-414A-8825-D0FF9C2E3D78")]
    public readonly InputSlot<string> Replace = new();
    
    [Input(Guid = "3C65F1D1-D535-4ED8-885D-78A3D1BF26BC")]
    public readonly InputSlot<bool> UseRegex = new();
}