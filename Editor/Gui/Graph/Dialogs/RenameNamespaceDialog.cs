#nullable enable
using ImGuiNET;
using T3.Core.SystemUi;
using T3.Editor.Gui.Graph.Dialogs;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.SymbolLib;
using T3.Editor.UiModel;

namespace T3.Editor.Gui.Dialogs;

internal sealed class RenameNamespaceDialog : ModalDialog
{
    internal void Draw(NamespaceTreeNode subtreeNodeToRename)
    {
        if (BeginDialog("Move or rename namespace"))
        {
            if (ImGui.IsWindowAppearing())
            {
                _node = subtreeNodeToRename;
                _nameSpace = _node.Namespace;
                EditableSymbolProject.TryGetEditableProjectOfNamespace(_nameSpace, out _projectToCopyFrom);
                _projectToCopyTo = _projectToCopyFrom;
            }

            var canRename = false;
            var namespaceValid = false;
            var hasProjectToCopyFrom = _projectToCopyFrom != null;
            if (hasProjectToCopyFrom)
            {
                SymbolModificationInputs.DrawProjectDropdown(ref _nameSpace, ref _projectToCopyTo);

                if (_projectToCopyTo != null)
                {
                    canRename = DrawRenameFields(out namespaceValid);
                }
            }
            else
            {
                ImGui.TextColored(UiColors.StatusError.Rgba, $"No source project found for namespace {_nameSpace}");
            }

            if (canRename)
            {
                AlignCtaButtons("Cancel", "Rename");
            }
            else
            {
                AlignCtaButtons("Cancel");
            }

            if (CustomComponents.DrawCtaButton("Cancel", Icon.None, CustomComponents.ButtonStates.Emphasized))
            {
                Close();
            }

            if (canRename)
            {
                ImGui.SameLine();
                if (CustomComponents.DrawCtaButton("Rename", namespaceValid))
                {
                    RenameNamespace();
                }
            }

            EndDialogContent();
        }

        EndDialog();
    }

    /** Returns false if the project data is incomplete and no rename can be offered. */
    private static bool DrawRenameFields(out bool namespaceValid)
    {
        namespaceValid = false;
        if (ImGui.IsWindowAppearing())
            ImGui.SetKeyboardFocusHere();

        if (_projectToCopyTo == null || _node == null || _projectToCopyFrom == null)
        {
            ImGui.Text("invalid project data");
            return false;
        }
        
        _ = SymbolModificationInputs.DrawNamespaceInput(ref _nameSpace, _projectToCopyTo, false, out namespaceValid);

        CustomComponents.HelpText("Careful now. This operator might affect a lot of operator definitions");
        return true;
    }

    private static void RenameNamespace()
    {
        if (_projectToCopyTo == null || _node == null || _projectToCopyFrom == null)
            return;

        if (EditableSymbolProject.RenameNameSpaces(_node, _projectToCopyFrom, _projectToCopyTo, _nameSpace, out var reason))
        {
            T3Ui.Save(false);
        }
        else
        {
            BlockingWindow.Instance.ShowMessageBox(reason, "Could not rename namespace");
        }

        Close();
    }

    private static void Close()
    {
        ImGui.CloseCurrentPopup();
        _node = null;
        _projectToCopyTo = null;
        _projectToCopyFrom = null;
    }

    private static NamespaceTreeNode? _node;
    private static string _nameSpace= string.Empty;
    private static EditableSymbolProject? _projectToCopyFrom;
    private static EditableSymbolProject? _projectToCopyTo;
}