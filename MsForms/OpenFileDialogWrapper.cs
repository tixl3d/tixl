using T3.SystemUi;

namespace T3.MsForms;

internal sealed class OpenFileDialogWrapper : IFilePicker
{
    private readonly OpenFileDialog _dialog;

    internal OpenFileDialogWrapper()
    {
        _dialog = new();
    }

    public void Dispose()
    {
        _dialog.Dispose();
    }

    public string FileName { get => _dialog.FileName; set => _dialog.FileName = value; }
    public string Filter { get => _dialog.Filter; set => _dialog.Filter = value; }
    public string InitialDirectory { get => _dialog.InitialDirectory; set => _dialog.InitialDirectory = value; }
    public bool Multiselect { get => _dialog.Multiselect; set => _dialog.Multiselect = value; }
    public bool RestoreDirectory { get => _dialog.RestoreDirectory; set => _dialog.RestoreDirectory = value; }
    public bool ShowHelp { get => _dialog.ShowHelp; set => _dialog.ShowHelp = value; }
    public bool ShowReadOnly { get => _dialog.ShowReadOnly; set => _dialog.ShowReadOnly = value; }
    public string Title { get => _dialog.Title; set => _dialog.Title = value; }
    public bool ValidateNames { get => _dialog.ValidateNames; set => _dialog.ValidateNames = value; }

    public bool ChooseFile()
    {
        return _dialog.ShowDialog() == DialogResult.OK;
    }

    public bool CheckFileExists { get => _dialog.CheckFileExists; set => _dialog.CheckFileExists = value; }
    public bool CheckPathExists { get => _dialog.CheckPathExists; set => _dialog.CheckPathExists = value; }
    public int FilterIndex { get => _dialog.FilterIndex; set => _dialog.FilterIndex = value; }
}