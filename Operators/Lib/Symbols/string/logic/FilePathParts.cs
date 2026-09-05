namespace Lib.@string.logic;

[Guid("42c556fb-014b-4ac5-b390-f426ab415aa7")]
internal sealed class FilePathParts : Instance<FilePathParts>, IStatusProvider
{
    [Output(Guid = "A628FB9E-7647-4FEE-838D-F17395E15148")]
    public readonly Slot<string> Directory = new();
        
        
    [Output(Guid = "1242E534-DA34-4DA0-8C17-6CFA8FEF6E59")]
    public readonly Slot<string> FilenameWithoutExtension = new();
        
    [Output(Guid = "1a9be39d-16ed-4f5f-916d-03b604101c7e")]
    public readonly Slot<string> Extension = new();
        
    [Output(Guid = "DD5D3B87-D27D-405E-B8F4-524F3C18379C")]
    public readonly Slot<bool> FileExists = new();


    public FilePathParts()
    {
        FilenameWithoutExtension.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var path = FilePath.GetValue(context);
        if (!string.IsNullOrEmpty(path))
        {
            try
            {
                FileExists.Value = File.Exists(path);
                Directory.Value = Path.GetDirectoryName(path);
                Extension.Value = Path.GetExtension(path);
                FilenameWithoutExtension.Value = Path.GetFileNameWithoutExtension(path);
                _errorMessageForStatus = string.Empty;
                return;
            }
            catch (Exception e)
            {
                _errorMessageForStatus = "Failed to analyse filepath: " + e.Message;
                Log.Debug(_errorMessageForStatus, this);
                Reset();
                return;
            }
        }
            
        Reset();
        _errorMessageForStatus = "Need path";
        //Log.Debug("Need path", this);
    }

    private void Reset()
    {
        FileExists.Value = false;
        Directory.Value = null;
        FilenameWithoutExtension.Value = null;
        Extension.Value = null;
    }
        
    public IStatusProvider.StatusLevel GetStatusLevel()
    {
        return string.IsNullOrEmpty(_errorMessageForStatus) ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Error;
    }

    public string GetStatusMessage()
    {
        return _errorMessageForStatus;
    }

    private string _errorMessageForStatus;

    [Input(Guid = "04d5f714-4e38-4a1e-b245-f6f0b582b35a")]
    public readonly InputSlot<string> FilePath = new();


}