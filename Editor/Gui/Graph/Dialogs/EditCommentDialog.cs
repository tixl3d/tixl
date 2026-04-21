using ImGuiNET;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.Dialogs;

internal sealed class EditCommentDialog : ModalDialog
{
    public void Draw(NodeSelection selection)
    {
        DialogSize = new Vector2(500, 450);

        if (BeginDialog("Edit comment"))
        {
            var instance = selection.GetSelectedInstanceWithoutComposition();

            if (instance?.Parent == null)
            {
                CustomComponents.EmptyWindowMessage("Please select operator\nto add comment");
            }
            else
            {
                var symbolUi = instance.Parent.Symbol.GetSymbolUi();
                var symbolChildUi = symbolUi.ChildUis[instance.SymbolChildId];
                if (symbolChildUi == null)
                {
                    CustomComponents.EmptyWindowMessage("Unable to find a UI definition for the operator.");
                }
                else
                {
                    if (ImGui.IsWindowAppearing())
                    {
                        _editBuffer = symbolChildUi.Comment ?? string.Empty;
                        _originalComment = _editBuffer;
                        _editingChildUi = symbolChildUi;
                        _editingParentSymbolId = instance.Parent.Symbol.Id;
                    }

                    ImGui.PushFont(Fonts.FontLarge);
                    ImGui.Text(symbolChildUi.SymbolChild.Symbol.Name);
                    ImGui.PopFont();

                    if (ImGui.IsWindowAppearing())
                        ImGui.SetKeyboardFocusHere();

                    ImGui.InputTextMultiline("##comment", ref _editBuffer, 2000, new Vector2(-1, 300), ImGuiInputTextFlags.None);
                }
            }
            if (ImGui.Button("Close"))
            {
                CommitEdits();
                ImGui.CloseCurrentPopup();
            }

            EndDialogContent();
        }
        else if (_editingChildUi != null)
        {
            // Dialog closed via ESC or clicking outside — commit any pending edits so they aren't lost.
            CommitEdits();
        }
        EndDialog();
    }

    private void CommitEdits()
    {
        if (_editingChildUi == null)
            return;

        if (_editBuffer != _originalComment)
        {
            UndoRedoStack.AddAndExecute(new ChangeCommentCommand(_editingChildUi, _editingParentSymbolId, _editBuffer));
        }

        _editingChildUi = null;
        _editBuffer = string.Empty;
        _originalComment = string.Empty;
    }

    private SymbolUi.Child? _editingChildUi;
    private Guid _editingParentSymbolId;
    private string _editBuffer = string.Empty;
    private string _originalComment = string.Empty;
}
