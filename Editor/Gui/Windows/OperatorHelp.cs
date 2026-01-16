#nullable enable
using System.IO;
using System.Text.RegularExpressions;
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.SystemUi;
using T3.Core.Utils;
using T3.Editor.Gui.Dialogs;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Helpers;
using T3.Editor.UiModel.InputsAndTypes;

namespace T3.Editor.Gui.Windows;

internal sealed class OperatorHelp
{
    public static bool DrawHelpIcon(SymbolUi symbolUi, ref bool isEnabled)
    {
        var changed = false;

        ImGui.SameLine();
        var w = ImGui.GetFrameHeight();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 2);
        var toggledToEdit = ImGui.GetIO().KeyCtrl;
        var icon = toggledToEdit ? Icon.PopUp : Icon.Help;
        ImGui.PushStyleColor(ImGuiCol.Button, Color.Transparent.Rgba);
        if (CustomComponents.IconButton(
                                        icon,
                                        new Vector2(w, w),
                                        isEnabled
                                            ? CustomComponents.ButtonStates.Activated
                                            : CustomComponents.ButtonStates.Dimmed
                                       ))
        {
            changed = true;
            if (toggledToEdit)
            {
                EditDescriptionDialog.ShowNextFrame();
            }
            else
            {
                isEnabled = !isEnabled;
            }
        }

        ImGui.PopStyleColor();

        if (ImGui.IsItemHovered() && !isEnabled)
        {
            _timeSinceTooltipHovered += ImGui.GetIO().DeltaTime;

            var delayedFadeInToPreventImguiFlickering = (_timeSinceTooltipHovered * 3).Clamp(0.001f, 1);
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, delayedFadeInToPreventImguiFlickering * ImGui.GetStyle().Alpha);
            ImGui.BeginTooltip();
            ImGui.Dummy(new Vector2(500 * T3Ui.UiScaleFactor, 1));

            DrawHelp(symbolUi);
            ImGui.EndTooltip();
            ImGui.PopStyleVar();
        }
        else
        {
            _timeSinceTooltipHovered = 0;
        }

        return changed;
    }

    public static bool DrawHelpSummary(SymbolUi symbolUi, bool showMoreIndicator = true)
    {
        if (string.IsNullOrEmpty(symbolUi.Description))
            return false;

        using var reader = new StringReader(symbolUi.Description);
        var firstLine = reader.ReadLine();
        if (string.IsNullOrEmpty(firstLine))
            return false;

        var helpRequested = false;

        //ImGui.Indent(10);

        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        ImGui.TextWrapped(firstLine);
        if (ImGui.IsItemHovered())
        {
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                EditDescriptionDialog.ShowNextFrame();
            }

            CustomComponents.TooltipForLastItem("Click to edit descriptions and links");
        }

        ImGui.PopStyleColor();

        if (showMoreIndicator)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Fade(0.5f).Rgba);
            FormInputs.AddVerticalSpace(5);

            var anyParameterHasDescription = symbolUi.InputUis
                                                     .Values
                                                     .Any(inputUi => !string.IsNullOrEmpty(inputUi.Description));

            if (firstLine != symbolUi.Description || anyParameterHasDescription)
            {
                ImGui.TextUnformatted("Read more...");
                if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    helpRequested = true;
                }
            }

            //FormInputs.AddVerticalSpace();
            ImGui.PopStyleColor();
        }

        return helpRequested;
    }

    public static void DrawHelp(SymbolUi symbolUi)
    {
        // Title and namespace
        ImGui.Indent(10);
        FormInputs.AddSectionHeader(symbolUi.Symbol.Name);
        ImGui.PushFont(Fonts.FontSmall);
        ImGui.TextUnformatted("in ");
        ImGui.SameLine();
        ImGui.TextUnformatted(symbolUi.Symbol.Namespace);
        ImGui.PopFont();
        ImGui.Unindent(10);
        FormInputs.SetIndentToLeft();
        FormInputs.AddVerticalSpace();

        // Description
        ImGui.PushFont(Fonts.FontNormal);

        ImGui.Indent(10);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(10, 10));

        if (!string.IsNullOrEmpty(symbolUi.Description))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
            ImGui.TextWrapped(symbolUi.Description);

            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered())
            {
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    EditDescriptionDialog.ShowNextFrame();
                }
            }

            CustomComponents.TooltipForLastItem("Click to edit description and links");
        }
        else
        {
            FormInputs.AddHint("No description yet.");
            if (ImGui.Button("Edit description... "))
                EditDescriptionDialog.ShowNextFrame();
        }

        _parametersWithDescription.Clear();

        foreach (var inputUi in symbolUi.InputUis.Values)
        {
            if (!string.IsNullOrEmpty(inputUi.Description))
            {
                _parametersWithDescription.Add(inputUi);
            }
        }

        // Parameter descriptions
        if (_parametersWithDescription.Count > 0)
        {
            FormInputs.AddVerticalSpace(5);

            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
            ImGui.PushFont(Fonts.FontNormal);
            ImGui.TextUnformatted("Parameters details");
            ImGui.PopFont();
            ImGui.PopStyleColor();

            var parameterColorWidth = 150f * T3Ui.UiScaleFactor;
            foreach (var p in _parametersWithDescription)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, UiColors.Text.Rgba);

                var parameterNameWidth = ImGui.CalcTextSize(p.InputDefinition.Name).X;
                ImGui.SetCursorPosX(parameterColorWidth - parameterNameWidth);
                ImGui.TextUnformatted(p.InputDefinition.Name);
                ImGui.PopStyleColor();

                ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
                ImGui.SameLine(parameterColorWidth + 10);
                ImGui.TextWrapped(p.Description);
                ImGui.PopStyleColor();
            }
        }

        ImGui.Dummy(Vector2.One);

        DocumentationRenderer.DrawLinksAndExamples(symbolUi);

        ImGui.PopStyleVar();
        ImGui.Unindent();
        ImGui.Dummy(new Vector2(10, 10));
        ImGui.PopFont();
    }

    public static class DocumentationRenderer
    {
        private static SymbolUi? _cachedSymbolUi;
        private static Guid _cachedSymbolId;

        private static readonly List<(string Title, string Description, string Url, Icon? Icon)> _cachedLinks = [];
        private static readonly List<(SymbolUi SymbolUi, string Name)> _cachedReferencedSymbols = [];
        private static int _cachedSymbolUiVersion = -1;

        public static void DrawLinksAndExamples(SymbolUi symbolUi)
        {
            // Check if symbolUi changed
            if (symbolUi != _cachedSymbolUi || symbolUi.Symbol.Id != _cachedSymbolId || symbolUi.VersionCounter != _cachedSymbolUiVersion)
            {
                _cachedSymbolUiVersion = symbolUi.VersionCounter;
                _cachedSymbolUi = symbolUi;
                _cachedSymbolId = symbolUi.Symbol.Id;
                CacheSymbolData(symbolUi);
            }

            ImGui.PushFont(Fonts.FontSmall);
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(5, 5));

            DrawLinks();
            DrawExamples();
            DrawReferencedSymbols();

            ImGui.PopFont();
            ImGui.PopStyleVar();
        }

        private static void CacheSymbolData(SymbolUi symbolUi)
        {
            _cachedLinks.Clear();
            _cachedReferencedSymbols.Clear();

            // Cache links
            foreach (var link in symbolUi.Links.Values)
            {
                if (!string.IsNullOrEmpty(link.Url))
                {
                    var title = link.Title ?? link.Type.ToString();
                    _cachedLinks.Add((title,
                                      link.Url,
                                      link.Description,
                                      ExternalLink.LinkIcons.TryGetValue(link.Type, out var icon) ? icon : (Icon?)null)
                                    );
                }
            }

            // Cache referenced symbols
            if (!string.IsNullOrEmpty(symbolUi.Description))
            {
                foreach (Match match in _itemRegex.Matches(symbolUi.Description))
                {
                    var referencedName = match.Groups[1].Value;

                    if (referencedName == symbolUi.Symbol.Name || _cachedReferencedSymbols.Any(x => x.Name == referencedName))
                        continue;

                    foreach (var symbol in EditorSymbolPackage.AllSymbols)
                    {
                        if (symbol.Name == referencedName)
                        {
                            var package = (EditorSymbolPackage)symbol.SymbolPackage;
                            if (package.TryGetSymbolUi(symbol.Id, out var exampleSymbolUi))
                            {
                                _cachedReferencedSymbols.Add((exampleSymbolUi, referencedName));
                            }

                            break;
                        }
                    }
                }
            }
        }

        private static void DrawLinks()
        {
            if (_cachedLinks.Count == 0) return;

            ImGui.AlignTextToFramePadding();
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
            ImGui.TextUnformatted("Links:");
            ImGui.PopStyleColor();
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Button, Color.Transparent.Rgba);

            foreach (var (title, url, description, icon) in _cachedLinks)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, UiColors.StatusAutomated.Rgba);
                bool clicked;

                if (icon.HasValue)
                {
                    ImGui.Button("    " + title);
                    Icons.DrawIconOnLastItem(icon.Value, UiColors.StatusAutomated, 0);
                    clicked = ImGui.IsItemClicked();
                }
                else
                {
                    clicked = ImGui.Button(title);
                }

                ImGui.PopStyleColor();
                CustomComponents.TooltipForLastItem(string.IsNullOrEmpty(description) ? _openLink : description + "\n" + _openLink, url);

                if (clicked)
                    CoreUi.Instance.OpenWithDefaultApplication(url);

                ImGui.SameLine();
            }

            ImGui.Dummy(new Vector2(2, 2));
            ImGui.PopStyleColor();
        }

        private static void DrawExamples()
        {
            if (ExampleSymbolLinking.TryGetExamples(_cachedSymbolUi!.Symbol.Id, out var examples))
            {
                foreach (var exampleUi in examples)
                {
                    UiElements.DrawExampleOperator(exampleUi, "Example");
                }
            }
        }

        private static void DrawReferencedSymbols()
        {
            if (_cachedReferencedSymbols.Count == 0) return;

            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
            ImGui.TextUnformatted("Also see:");
            ImGui.PopStyleColor();
            ImGui.Dummy(Vector2.One);

            foreach (var (exampleSymbolUi, referencedName) in _cachedReferencedSymbols)
            {
                UiElements.DrawExampleOperator(exampleSymbolUi, referencedName);
            }
        }
    }

    private static void DrawGroupLabel(string title)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        ImGui.TextUnformatted(title);
        ImGui.PopStyleColor();
    }

    internal static readonly EditSymbolDescriptionDialog EditDescriptionDialog = new();
    private static readonly Regex _itemRegex = new(@"\[([A-Za-z\d_]+)\]", RegexOptions.Compiled);

    private static readonly List<IInputUi> _parametersWithDescription = new(10);

    // public bool IsActive => _isDocumentationActive;
    // private bool _isDocumentationActive = false;
    private static float _timeSinceTooltipHovered = 0;
    private static readonly string _openLink = "Open link in browser:";
}