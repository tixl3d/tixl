#nullable enable
using System.IO;
using System.Text;
using ImGuiNET;
using T3.Core.Model;
using T3.Core.SystemUi;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Window;
using T3.Editor.UiModel;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.Dialogs;

/// <summary>
/// Shown once after startup when symbols use operators that could not be found (missing package,
/// deleted operator). Those operators are kept in the files, so this only informs: it names what is
/// missing where, and leads to the graphs that show the gaps.
/// </summary>
internal sealed class MissingOperatorsDialog : ModalDialog
{
    internal void ShowIfOperatorsAreMissing()
    {
        _entries.Clear();
        _missingOperatorCount = 0;

        foreach (var package in SymbolPackage.AllPackages)
        {
            // Read-only packages can't be fixed from here, so warning about them on every start would only
            // nag. Their gaps still show in the graph and in the startup log.
            if (package is not EditorSymbolPackage editorPackage || package.IsReadOnly)
                continue;

            foreach (var symbol in editorPackage.Symbols.Values)
            {
                if (!symbol.HasUnresolvedChildren)
                    continue;

                _missingOperatorCount += symbol.UnresolvedChildren.Count;
                editorPackage.TryGetSymbolFilePath(symbol, out var filePath);
                _entries.Add(new Entry(editorPackage, symbol.Id, symbol.Name, filePath, SummarizeNames(symbol)));
            }
        }

        if (_entries.Count == 0)
            return;

        _entries.Sort(static (a, b) =>
                      {
                          var byPackage = string.Compare(a.Package.DisplayName, b.Package.DisplayName, StringComparison.OrdinalIgnoreCase);
                          return byPackage != 0 ? byPackage : string.Compare(a.SymbolName, b.SymbolName, StringComparison.OrdinalIgnoreCase);
                      });
        ShowNextFrame();
    }

    public void Draw()
    {
        DialogSize = new Vector2(620, 380);
        if (BeginDialog("Warning: Missing Symbols"))
        {
            CustomComponents.StylizedText($"{_missingOperatorCount} operators in {_entries.Count} symbols could not be found",
                                          Fonts.FontBold, UiColors.Text);
            ImGui.SameLine();
            if (CustomComponents.IconButton(Icon.Help, new Vector2(ImGui.GetFrameHeight())))
                CoreUi.Instance.OpenWithDefaultApplication(HelpUrl);

            CustomComponents.TooltipForLastItem("Nothing is lost - missing operators stay in your files.",
                                                "Click to read what this means and what to do next.");
            FormInputs.AddVerticalSpace(6);

            // Fixed height: a child sized relative to the window would feed back into the dialog's auto-resize
            ImGui.BeginChild("##missingList", new Vector2(0, 250 * T3Ui.UiScaleFactor), ImGuiChildFlags.Borders);
            DrawEntries();
            ImGui.EndChild();

            FormInputs.AddVerticalSpace(6);
            if (CustomComponents.DrawCtaButton("Close", Icon.None, CustomComponents.ButtonStates.Default))
                ImGui.CloseCurrentPopup();

            EndDialogContent();
        }

        EndDialog();
    }

    private void DrawEntries()
    {
        EditorSymbolPackage? lastPackage = null;
        var buttonWidth = 110 * T3Ui.UiScaleFactor;
        var iconSize = new Vector2(ImGui.GetFrameHeight());
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var buttonsWidth = buttonWidth + iconSize.X + spacing;

        for (var index = 0; index < _entries.Count; index++)
        {
            var entry = _entries[index];
            if (entry.Package != lastPackage)
            {
                if (lastPackage != null)
                    FormInputs.AddVerticalSpace(6);

                CustomComponents.StylizedText(entry.Package.DisplayName, Fonts.FontSmall, UiColors.TextMuted);
                lastPackage = entry.Package;
            }

            ImGui.PushID(index);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(entry.SymbolName);
            ImGui.SameLine();

            var namesWidth = ImGui.GetContentRegionAvail().X - buttonsWidth - 2 * spacing;
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.StatusAttention.Rgba);
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + MathF.Max(namesWidth, 80 * T3Ui.UiScaleFactor));
            ImGui.TextUnformatted(entry.MissingNames);
            ImGui.PopTextWrapPos();
            ImGui.PopStyleColor();

            ImGui.SameLine(ImGui.GetWindowWidth() - buttonsWidth - ImGui.GetStyle().WindowPadding.X - ImGui.GetStyle().ScrollbarSize);
            if (ImGui.Button("Show in Graph", new Vector2(buttonWidth, 0)))
            {
                if (TryOpenInGraph(entry))
                    ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();
            var canReveal = entry.FilePath != null;
            if (CustomComponents.IconButton(Icon.FolderOpen, iconSize,
                                            canReveal ? CustomComponents.ButtonStates.Default : CustomComponents.ButtonStates.Disabled)
                && canReveal)
            {
                RevealFile(entry.FilePath!);
            }

            CustomComponents.TooltipForLastItem("Reveal in Explorer", entry.FilePath);
            ImGui.PopID();
        }
    }

    private static string SummarizeNames(T3.Core.Operator.Symbol symbol)
    {
        // Same operator used several times reads better as "Name x3"
        var counts = new Dictionary<string, int>();
        foreach (var child in symbol.UnresolvedChildren)
        {
            counts.TryGetValue(child.DisplayName, out var count);
            counts[child.DisplayName] = count + 1;
        }

        var builder = new StringBuilder();
        foreach (var (name, count) in counts)
        {
            if (builder.Length > 0)
                builder.Append(", ");

            builder.Append(name);
            if (count > 1)
                builder.Append(" x").Append(count);
        }

        return builder.ToString();
    }

    /// <summary>Opens the symbol as the graph's root and frames its missing operators.</summary>
    private static bool TryOpenInGraph(Entry entry)
    {
        if (!OpenedProject.TryCreateWithExplicitHome(entry.Package, entry.SymbolId, out var openedProject, out var failureLog))
        {
            Log.Warning($"Can't open [{entry.SymbolName}]: {failureLog}");
            return false;
        }

        GraphWindow? graphWindow = null;
        foreach (var window in GraphWindow.GraphWindowInstances)
        {
            if (!window.Config.Visible)
                continue;

            graphWindow = window;
            break;
        }

        graphWindow ??= GraphWindow.GraphWindowInstances.Count > 0 ? GraphWindow.GraphWindowInstances[0] : null;
        if (graphWindow == null || !graphWindow.TrySetToProject(openedProject, tryRestoreViewArea: false))
        {
            Log.Warning($"Can't open [{entry.SymbolName}]: no graph window available");
            return false;
        }

        if (!SymbolUiRegistry.TryGetSymbolUi(entry.SymbolId, out var symbolUi) || symbolUi.UnresolvedChildUiJsons.Count == 0)
            return true;

        var bounds = ImRect.RectWithSize(symbolUi.UnresolvedChildUiJsons[0].PosOnCanvas, SymbolUi.Child.DefaultOpSize);
        foreach (var (_, posOnCanvas, _) in symbolUi.UnresolvedChildUiJsons)
        {
            bounds.Add(ImRect.RectWithSize(posOnCanvas, SymbolUi.Child.DefaultOpSize));
        }

        bounds.Expand(300);
        graphWindow.ProjectView?.GraphView.Canvas.RequestTargetViewAreaWithTransition(bounds, Interaction.ScalableCanvas.Transition.Instant);
        return true;
    }

    private static void RevealFile(string filePath)
    {
        if (File.Exists(filePath))
        {
            CoreUi.Instance.RevealInFileBrowser(filePath);
            return;
        }

        Log.Warning($"Can't reveal missing file '{filePath}'");
    }

    private const string HelpUrl = "https://help.tixl.app/using/Pitfalls/#missing-operators";

    private sealed record Entry(EditorSymbolPackage Package, Guid SymbolId, string SymbolName, string? FilePath, string MissingNames);

    private readonly List<Entry> _entries = [];
    private int _missingOperatorCount;
}
