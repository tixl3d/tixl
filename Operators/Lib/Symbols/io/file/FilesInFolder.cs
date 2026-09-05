using T3.Core.Utils;

namespace Lib.io.file;

[Guid("f90fcd0a-eab9-4e2a-b393-e8d3a0380823")]
internal sealed class FilesInFolder : Instance<FilesInFolder>
{
    [Output(Guid = "99bd5b48-7a28-44a7-91e4-98b33cfda20f")]
    public readonly Slot<List<string>> Files = new();

    [Output(Guid = "a40ea23c-e64a-4cca-ae3c-d447dbf7ef93")]
    public readonly Slot<int> NumberOfFiles = new();


    public FilesInFolder()
    {
        Files.UpdateAction += Update;
        NumberOfFiles.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var wasTriggered = MathUtils.WasTriggered(TriggerUpdate.GetValue(context), ref _trigger);
        var folderIsDirty = Folder.DirtyFlag.IsDirty;
        if (wasTriggered || folderIsDirty || Filter.DirtyFlag.IsDirty)
        {
            TriggerUpdate.SetTypedInputValue(false);
                
            if (folderIsDirty)
            {
                var folderPath = Folder.GetValue(context);
                var success = TryGetFilePath(folderPath, out var resolvedFolder, true);
                _resolvedFolder = success ? resolvedFolder : folderPath;
            }
                
            var filter = Filter.GetValue(context);
            var filePaths = Directory.Exists(_resolvedFolder) 
                                ? Directory.GetFiles(_resolvedFolder).ToList() 
                                : new List<string>();
                
            Files.Value = string.IsNullOrEmpty(Filter.Value) 
                              ? filePaths 
                              : filePaths.FindAll(filepath => filepath.Contains(filter)).ToList();
                
            NumberOfFiles.Value = Files.Value.Count;
        }
    }

    private bool _trigger;
    private string _resolvedFolder;

    [Input(Guid = "ca9778e7-072c-4304-9043-eeb2dc4ca5d7")]
    public readonly InputSlot<string> Folder = new(".");
        
    [Input(Guid = "8B746651-16A5-4274-85DB-0168D30C86B2")]
    public readonly InputSlot<string> Filter = new("*.png");
        
    [Input(Guid = "E14A4AAE-E253-4D14-80EF-A90271CD306A")]
    public readonly InputSlot<bool> TriggerUpdate = new();

}