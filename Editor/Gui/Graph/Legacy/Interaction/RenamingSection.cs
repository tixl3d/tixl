#nullable enable
using ImGuiNET;
using T3.Editor.App;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Sections;
using T3.SystemUi;

namespace T3.Editor.Gui.Legacy.Interaction;

internal static class RenamingSection
{
    internal static void Draw(Section section, ImRect screenArea, bool shouldBeOpened)
    {
        var justOpened = false;
        if (_focusedSectionId == Guid.Empty)
        {
            if (shouldBeOpened)
            {
                justOpened = true;
                ImGui.SetKeyboardFocusHere();
                _focusedSectionId = section.Id;
                _changeSectionTextCommand = new ChangeSectionTextCommand(section, section.Title);
            }
        }

        if (_focusedSectionId == Guid.Empty)
            return;

        if (_focusedSectionId != section.Id)
            return;

        var positionInScreen = screenArea.Min;
        ImGui.SetCursorScreenPos(positionInScreen);

        var text = section.Title;

        ImGui.SetNextItemWidth(150);
        ImGui.InputTextMultiline("##renameSection", ref text, 256, screenArea.GetSize(), ImGuiInputTextFlags.AutoSelectAll);
        if (!ImGui.IsItemDeactivated())
            section.Title = text;

        if (!justOpened 
            && (ImGui.IsItemDeactivated() || ImGui.IsKeyPressed(Key.Esc.ToImGuiKey()))
            && _changeSectionTextCommand != null)
        {
            _focusedSectionId = Guid.Empty;
            _changeSectionTextCommand.NewText = section.Title;
            UndoRedoStack.AddAndExecute(_changeSectionTextCommand);
        }
    }

    private static Guid _focusedSectionId;
    private static ChangeSectionTextCommand? _changeSectionTextCommand;
}