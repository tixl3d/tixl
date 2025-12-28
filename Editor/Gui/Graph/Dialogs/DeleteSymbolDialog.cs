#nullable enable
using System.Collections.Immutable;
using ImGuiNET;
using T3.Core.Operator;
using T3.Core.SystemUi;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Helpers;

namespace T3.Editor.Gui.Dialogs;

internal sealed partial class DeleteSymbolDialog : ModalDialog
{
    /// <summary>
    /// Renders the main dialog content including dependency analysis and delete/cancel buttons.
    /// </summary>
    /// <param name="symbol">The symbol currently selected for deletion.</param>
    internal void Draw(Symbol symbol)
    {
        if (!BeginDialog("Delete Symbol"))
        {
            EndDialog();
            return;
        }

        var dialogJustOpened = _symbol == null;
        var symbolChanged = _symbol != null && (dialogJustOpened || _symbol.Id != symbol.Id);

        if (dialogJustOpened || symbolChanged)
        {
            _symbol = symbol;
            _cachedMatches = null;  // Break cache on any symbol switch
            _allowDeletion = false; // Reset until re-analyzed
        }

        LocalSymbolInfo? info = null;
        if (SymbolAnalysis.TryGetSymbolInfo(symbol, out var analyzedInfo, true))
        {
            info = new LocalSymbolInfo(analyzedInfo);
            DrawAnalysisUi(symbol, info);
        }
        else
        {
            ImGui.Separator();
            ImGui.TextColored(UiColors.TextMuted, "Could not analyze dependencies for this symbol.");
            _allowDeletion = false;
        }

        ImGui.Separator();
        FormInputs.AddVerticalSpace();

        // Only draw buttons if deletion is allowed
        if (_allowDeletion)
        {
            var buttonLabel = "Delete";
            if (info is { DependingSymbols.IsEmpty: false })
            {
                buttonLabel = "Force delete";
            }
            
            if (ImGui.Button(buttonLabel))
            {
                var success = info is { DependingSymbols.IsEmpty: false } 
                                  ? DeleteSymbol(symbol, info.DependingSymbols.ToHashSet(), out var reason) 
                                  : DeleteSymbol(symbol, null, out reason);
    
                if (!success)
                {
                    BlockingWindow.Instance.ShowMessageBox(reason, "Could not delete symbol");
                }
                else
                {
                    Close();
                }
            }

            ImGui.SameLine();
        }

        if (ImGui.Button("Cancel"))
        {
            Close();
        }
        
        EndDialogContent();
        EndDialog();
    }
    
    /// <summary>
    /// Lightweight container for symbol dependency and classification data used by the dialog,
    /// derived from <see cref="SymbolAnalysis.SymbolInformation"/>.
    /// </summary>
    private sealed class LocalSymbolInfo(SymbolAnalysis.SymbolInformation source)
    {
        public ImmutableHashSet<Guid> DependingSymbols { get; } = ImmutableHashSet.CreateRange(source.DependingSymbols);
        public int UsageCount { get; } = source.UsageCount;
        public SymbolAnalysis.OperatorClassification OperatorType { get; } = source.OperatorType;
    }

    /// <summary>
    /// Closes the delete-symbol dialog and resets its internal state so a new delete session
    /// starts with a clean symbol reference and deletion flag.
    /// </summary>
    private static void Close()
    {
        ImGui.CloseCurrentPopup();
        _symbol = null;
        _allowDeletion = false;
        _cachedMatches = null;
    }

    private static Symbol? _symbol;
    private static bool _allowDeletion;
    private static List<SymbolUi>? _cachedMatches;
}
