namespace Lib.@string.search;

[Guid("fc0a5e68-9915-4323-b2a4-2491fa5d59a9")]
internal sealed class IndexOf : Instance<IndexOf>
{
    [Input(Guid = "841784c4-0ca7-41cd-8d79-bbe4989e0842")]
    public readonly InputSlot<string> OriginalString = new();

    [Input(Guid = "81a7aa30-eab9-4637-bb11-7c0940460afb")]
    public readonly InputSlot<string> SearchPattern = new();

    public IndexOf()
    {
        Index.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        string searchPattern = SearchPattern.GetValue(context);
        string originalString = OriginalString.GetValue(context);
        if (string.IsNullOrEmpty(searchPattern) || string.IsNullOrEmpty(originalString))
        {
            Index.Value = -1;
            return;
        }

        try
        {
            Index.Value = originalString.IndexOf(searchPattern);
        }
        catch (Exception)
        {
            Log.Error($"'{originalString}' or '{searchPattern}' is incorrect", this);
        }
    }

    [Output(Guid = "4bb4bb23-4c3f-4d7d-9dab-c37ac63dd1c9")]
    public readonly Slot<int> Index = new Slot<int>();
}