using System.Numerics;
using ImGuiNET;

namespace SilkWindows.Implementations.FileManager;

internal sealed class NewSubfolderWindow(DirectoryInfo directoryInfo) : IImguiDrawer<DirectoryInfo>
{
    public void Init()
    {
    }

    public void OnRender(string windowName, double deltaSeconds, ImFonts fonts)
    {
        ImGui.BeginChild(directoryInfo.FullName + '/');

        // Top margin
        ImGui.Dummy(new Vector2(0, 20));

        // Left margin using indent
        ImGui.Indent(20);

        ImGui.PushFont(fonts.Large);
        ImGui.Text("Enter name for new subfolder:");

        // Spacing between label and input
        ImGui.Dummy(new Vector2(0, 10));
       
        ImGui.SetNextItemWidth(ImGui.GetWindowWidth()-40); // Set a specific width for the input field

        if (ImGui.InputText("##newSubfolderInput", ref _newSubfolderInput, 32))
        {
            _inputValid = _newSubfolderInput.Length > 0
                          && _newSubfolderInput.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
        }

        // Spacing between input and button
        ImGui.Dummy(new Vector2(0, 15));

        if (!_inputValid)
            ImGui.BeginDisabled();

        // Button colors for visual feedback
        var buttonColor = new Vector4(0.2f, 0.6f, 0.9f, 1f);
        var buttonHoverColor = new Vector4(0.3f, 0.7f, 1f, 1f);
        var buttonActiveColor = new Vector4(0.15f, 0.5f, 0.8f, 1f);

        ImGui.PushStyleColor(ImGuiCol.Button, buttonColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, buttonHoverColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, buttonActiveColor);

        // Button padding
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(20, 10));

        if (ImGui.Button("Create"))
        {
            try
            {
                var newSubdirectory = directoryInfo.CreateSubdirectory(_newSubfolderInput);
                if (newSubdirectory.Exists)
                {
                    Result = newSubdirectory;
                    _shouldClose = true;
                    _errorText = "";
                }
                else
                {
                    _errorText = "Failed to create subfolder";
                }
            }
            catch (Exception e)
            {
                _errorText = e.Message;
            }
        }

        ImGui.PopStyleVar(); // Frame padding
        ImGui.PopStyleColor(3); // Button colors
        ImGui.PopFont();

        if (!_inputValid)
            ImGui.EndDisabled();

        if (!string.IsNullOrWhiteSpace(_errorText))
        {
            ImGui.Dummy(new Vector2(0, 10));
            var errorColor = new Vector4(1f, 0.1f, 0.1f, 1f);
            ImGui.PushFont(fonts.Bold);
            ImGui.PushStyleColor(ImGuiCol.Text, errorColor);
            ImGui.Text(_errorText);
            ImGui.PopStyleColor();
            ImGui.PopFont();
        }

        ImGui.Unindent(20);

        ImGui.EndChild();
    }

    public void OnWindowUpdate(double deltaSeconds, out bool shouldClose)
    {
        shouldClose = _shouldClose;

    }

    public void OnClose()
    {

    }

    public void OnFileDrop(string[] filePaths)
    {

    }

    public void OnWindowFocusChanged(bool changedTo)
    {

    }

    public DirectoryInfo? Result { get; private set; }

    private string _errorText = "";
    private bool _shouldClose;
    private bool _inputValid;
    private string _newSubfolderInput = "";
}