using ImGuiNET;
using T3.Core.Operator;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;

namespace T3.Editor.Gui.Dialogs;

public sealed class EditSymbolDescriptionDialog : ModalDialog
{
    /// <summary>Sets the symbol whose description the dialog edits — callers pass the symbol they are
    /// showing, which is not necessarily the graph selection (e.g. the Help window's current topic).</summary>
    public void ShowNextFrame(Symbol symbol)
    {
        _symbolIdToEdit = symbol.Id;
        ShowNextFrame();
    }

    public void Draw()
    {
        DialogSize = new Vector2(1100, 700);

        if (BeginDialog("Edit description"))
        {
            if (!SymbolUiRegistry.TryGetSymbolUi(_symbolIdToEdit, out var symbolUi))
            {
                // Symbol vanished while the dialog was open (project closed, package reload) — abandon the edit.
                _editingSymbolId = Guid.Empty;
                ImGui.CloseCurrentPopup();
                EndDialogContent();
                EndDialog();
                return;
            }

            if (ImGui.IsWindowAppearing())
            {
                _editingSymbolId = symbolUi.Symbol.Id;
                _originalDescription = symbolUi.Description ?? string.Empty;
                _originalLinks.Clear();
                foreach (var link in symbolUi.Links.Values)
                    _originalLinks.Add(link.Clone());
            }

            var desc = symbolUi.Description ?? string.Empty;

            ImGui.PushFont(Fonts.FontLarge);
            ImGui.Text(symbolUi.Symbol.Name);
            ImGui.PopFont();

            if (ImGui.IsWindowAppearing())
                ImGui.SetKeyboardFocusHere();

            ImGui.InputTextMultiline("##name", ref desc, 2000, new Vector2(-1, 400), ImGuiInputTextFlags.None);
            symbolUi.Description = desc;

            ImGui.Text("Links...");
            foreach (var l in symbolUi.Links.Values)
            {
                ImGui.PushID(l.Id.GetHashCode());

                ImGui.SetNextItemWidth(150);
                FormInputs.DrawEnumDropdown(ref l.Type, "type");

                ImGui.SameLine();
                CustomComponents.DrawInputFieldWithPlaceholder("URL", ref l.Url, 220);

                ImGui.SameLine();
                CustomComponents.DrawInputFieldWithPlaceholder("Title", ref l.Title, 220);

                ImGui.SameLine();
                CustomComponents.DrawInputFieldWithPlaceholder("Description", ref l.Description);

                ImGui.SameLine();
                if (CustomComponents.IconButton(Icon.Trash, Vector2.One * ImGui.GetFrameHeight()))
                {
                    symbolUi.Links.Remove(l.Id);
                    ImGui.PopID();
                    break; // prevent further iteration on dict
                }
                ImGui.PopID();
            }

            if (ImGui.Button("Add link"))
            {
                var newLink = new ExternalLink { Type = ExternalLink.LinkTypes.TutorialVideo };
                symbolUi.Links.Add(newLink.Id, newLink);
            }

            if (ImGui.Button("Close"))
            {
                CommitEdits(symbolUi);
                ImGui.CloseCurrentPopup();
            }

            EndDialogContent();
        }
        else if (_editingSymbolId != Guid.Empty)
        {
            // Dialog was closed via ESC or clicking outside — commit any pending edits.
            if (SymbolUiRegistry.TryGetSymbolUi(_editingSymbolId, out var symbolUi))
                CommitEdits(symbolUi);
            else
                _editingSymbolId = Guid.Empty;
        }

        EndDialog();
    }

    private void CommitEdits(SymbolUi symbolUi)
    {
        if (_editingSymbolId == Guid.Empty)
            return;

        var currentDescription = symbolUi.Description ?? string.Empty;
        var descriptionChanged = currentDescription != _originalDescription;
        var linksChanged = !LinksEqual(_originalLinks, symbolUi.Links);

        if (descriptionChanged || linksChanged)
        {
            // Snapshot "after" state first — the live dict is still the user-edited version.
            var newLinksSnapshot = new List<ExternalLink>(symbolUi.Links.Count);
            foreach (var link in symbolUi.Links.Values)
                newLinksSnapshot.Add(link.Clone());

            // Push without executing: the model already reflects the new state, so Do() would be
            // a no-op now but must still be a valid forward step for a future Redo.
            UndoRedoStack.Add(new ChangeSymbolDescriptionCommand(
                                  _editingSymbolId,
                                  _originalDescription, _originalLinks,
                                  currentDescription, newLinksSnapshot));

            // Do() isn't executed above, so its FlagAsModified never runs — mark the symbol dirty here.
            symbolUi.FlagAsModified();
        }

        _editingSymbolId = Guid.Empty;
        _originalLinks.Clear();
        _originalDescription = string.Empty;
    }

    private static bool LinksEqual(List<ExternalLink> a, OrderedDictionary<Guid, ExternalLink> b)
    {
        if (a.Count != b.Count)
            return false;

        var i = 0;
        foreach (var liveLink in b.Values)
        {
            var snap = a[i++];
            if (snap.Id != liveLink.Id
                || snap.Type != liveLink.Type
                || snap.Url != liveLink.Url
                || snap.Title != liveLink.Title
                || snap.Description != liveLink.Description)
            {
                return false;
            }
        }
        return true;
    }

    private Guid _symbolIdToEdit;
    private Guid _editingSymbolId;
    private string _originalDescription = string.Empty;
    private readonly List<ExternalLink> _originalLinks = [];
}
