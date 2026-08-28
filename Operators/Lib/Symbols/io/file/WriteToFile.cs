using T3.Core.Resource.Assets;
using T3.Core.Stats;

namespace Lib.io.file;

[Guid("0db15e2d-b457-44d7-bb58-ace0a0708073")]
internal sealed class WriteToFile : Instance<WriteToFile>
{
    [Output(Guid = "b5627217-63cf-49c6-b864-3f9af74b7a94")]
    public readonly Slot<string> Result = new();

    [Output(Guid = "D6234491-B051-4387-895A-6FA8C3C8AC37", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<string> OutFilepath = new();

    public WriteToFile()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var content = Content.GetValue(context);
        var filepath = Filepath.GetValue(context);

        if (content != _lastContent || filepath != _lastFilepath)
        {
            _lastContent = content;
            _lastFilepath = filepath;
            TryWrite(filepath, content);
        }

        Result.Value = content;
        OutFilepath.Value = filepath;    // Forward so it can be triggered
    }

    private void TryWrite(string filepath, string content)
    {
        if (!AssetRegistry.TryResolveAddressForWriting(filepath, this, out var absolutePath, out var failureReason))
        {
            this.LogErrorState(failureReason);
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
            File.WriteAllText(absolutePath, content ?? string.Empty);
            this.ClearErrorState();
        }
        catch (Exception e)
        {
            this.LogErrorState($"Failed to write file {absolutePath}: {e.Message}");
        }
    }

    [Input(Guid = "a12d0e5c-a0f9-4d3c-8ab6-827fb618c021")]
    public readonly InputSlot<string> Content = new();

    [Input(Guid = "DB4B08DA-9993-453A-A957-679637CDFD08")]
    public readonly InputSlot<string> Filepath = new();

    private string _lastContent;
    private string _lastFilepath;
}
