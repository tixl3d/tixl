using System.Globalization;

namespace Lib.@string.list;

[Guid("04d557f8-3cc7-471f-8f2f-39090fec63bb")]
internal sealed class ZipStringList : Instance<ZipStringList>
{
    [Output(Guid = "0fc84451-81a3-4fb1-bacf-ea22a4a341c7")]
    public readonly Slot<List<string>> Output = new();

    public ZipStringList()
    {
        Output.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var strOne = StringsOne.GetValue(context);
        var strTwo = StringsTwo.GetValue(context);
        if (strOne == null || strTwo == null)
        {
            Output.Value = [];
            return;
        }

        Output.Value = [.. strOne
            .Zip(strTwo, (a, b) => new[] {a, b})
            .SelectMany(t => t)];
    }

    [Input(Guid = "be829559-ab5b-41ee-b104-dc5b0c1a1b2e")]
    public readonly InputSlot<List<string>> StringsOne = new();

    [Input(Guid = "d69c10f6-6b3d-4624-a9c4-9ac4796290cf")]
    public readonly InputSlot<List<string>> StringsTwo = new();
}