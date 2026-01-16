#nullable enable

using ImGuiNET;
using T3.Editor.Compilation;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;

namespace T3.Editor.Gui.Dialogs;


internal sealed class RenameSymbolDialog : ModalDialog
{
    internal void Draw(IEnumerable<SymbolUi.Child> selectedChildUis2, ref string name)
    {
        if (BeginDialog("Rename symbol"))
        {
            var selectedChildUis = selectedChildUis2.ToList();
            
            if (selectedChildUis.Count != 1)
            {
                Log.Warning("Can't use RenameSymbolDialog without selected operator");
                ImGui.CloseCurrentPopup();
                return;
            }
            
            var symbolUi = selectedChildUis[0];
            var symbolChild = symbolUi.SymbolChild;
            if (symbolChild == null || symbolChild.Symbol.SymbolPackage.IsReadOnly)
            {
                Log.Warning("Can't use RenameSymbolDialog without selected operator");
                ImGui.CloseCurrentPopup();
                return;
            }
            ImGui.PushFont(Fonts.FontSmall);
            ImGui.TextUnformatted("Name");
            ImGui.PopFont();

            ImGui.SetNextItemWidth(150);

            
            var symbol = symbolUi.SymbolChild.Symbol;
            _ = SymbolModificationInputs.DrawSymbolNameInput(ref name, 
                                                             symbol.Namespace, 
                                                             symbol.SymbolPackage, 
                                                             ImGui.IsWindowAppearing(), 
                                                             out var isNameValid);

            ImGui.Spacing();
                
            if (CustomComponents.DisablableButton("Rename", isNameValid))
            {
                SymbolNaming.RenameSymbol(symbol, name);
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
            {
                ImGui.CloseCurrentPopup();
            }

            EndDialogContent();
        }
        EndDialog();
    }
}