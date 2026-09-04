#nullable enable
using System.IO;
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.SystemUi;
using T3.Core.Utils;
using T3.Editor.Gui.Dialogs;
using T3.Editor.Gui.Help;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.Styling.Markdown;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Helpers;
using T3.Editor.UiModel.InputsAndTypes;

namespace T3.Editor.Gui.Windows;

internal sealed class OperatorHelp
{
    /// <summary>
    /// The help icon in the Parameter window header: clicking toggles the Help window (opening it on the
    /// operator's doc); Ctrl-click edits the description. Hovering shows the doc as a tooltip while the
    /// Help window is closed.
    /// </summary>
    public static void DrawHelpIcon(SymbolUi symbolUi)
    {
        ImGui.SameLine();
        var w = ImGui.GetFrameHeight();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 2);
        var toggledToEdit = ImGui.GetIO().KeyCtrl;
        var icon = toggledToEdit ? Icon.PopUp : Icon.Help;
        var helpWindowIsOpen = HelpWindow.IsOpen;
        ImGui.PushStyleColor(ImGuiCol.Button, Color.Transparent.Rgba);
        if (CustomComponents.IconButton(icon, new Vector2(w, w),
                                        helpWindowIsOpen
                                            ? CustomComponents.ButtonStates.Activated
                                            : CustomComponents.ButtonStates.Default))
        {
            if (toggledToEdit)
                EditDescriptionDialog.ShowNextFrame(symbolUi.Symbol);
            else if (helpWindowIsOpen)
                HelpWindow.CloseWindow();
            else
                HelpWindow.ShowTopic(HelpTopic.ForOperator(symbolUi.Symbol.Id), showWindow: true);
        }

        ImGui.PopStyleColor();

        // While closed, the doc tooltip below carries the affordance; a second tooltip would overlap it.
        if (helpWindowIsOpen)
            CustomComponents.TooltipForLastItem("Toggle help window");

        // With the Help window open it already shows the selected op's doc — a tooltip would double it.
        if (ImGui.IsItemHovered() && !HelpWindow.IsOpen)
        {
            _timeSinceTooltipHovered += ImGui.GetIO().DeltaTime;

            var delayedFadeInToPreventImguiFlickering = (_timeSinceTooltipHovered * 3).Clamp(0.001f, 1);
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, delayedFadeInToPreventImguiFlickering * ImGui.GetStyle().Alpha);
            ImGui.BeginTooltip();
            ImGui.Dummy(new Vector2(500 * T3Ui.UiScaleFactor, 1));

            DrawHelp(symbolUi, isInTooltip: true);

            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
            ImGui.TextUnformatted("Click to open the Help window");
            ImGui.PopStyleColor();

            ImGui.EndTooltip();
            ImGui.PopStyleVar();
        }
        else
        {
            _timeSinceTooltipHovered = 0;
        }
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
                EditDescriptionDialog.ShowNextFrame(symbolUi.Symbol);
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

    /// <summary>Title + namespace block, exposed so the Help window can lay a thumbnail beside it.</summary>
    public static void DrawHeader(SymbolUi symbolUi)
    {
        ImGui.Indent(10);
        FormInputs.AddSectionHeader(symbolUi.Symbol.Name);
        ImGui.PushFont(Fonts.FontSmall);
        ImGui.TextUnformatted("in ");
        ImGui.SameLine();
        ImGui.TextUnformatted(symbolUi.Symbol.Namespace);
        ImGui.PopFont();
        ImGui.Unindent(10);
    }

    public static void DrawHelp(SymbolUi symbolUi, bool isInTooltip = false, bool omitHeader = false)
    {
        // Inside a tooltip the op-ref hover must not open its own (nested) tooltip.
        var opRefHandler = isInTooltip ? _operatorRefHandlerInTooltip : _operatorRefHandler;

        if (!omitHeader)
            DrawHeader(symbolUi);

        FormInputs.SetIndentToLeft();
        FormInputs.AddVerticalSpace();

        // Description
        ImGui.PushFont(Fonts.FontNormal);

        ImGui.Indent(10);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(10, 10));

        if (!string.IsNullOrEmpty(symbolUi.Description))
        {
            _descriptionView.Draw(symbolUi.Description,
                                  onOperatorRef: opRefHandler,
                                  operatorColor: MarkdownOperatorLinks.GetOperatorColor);
        }
        else
        {
            FormInputs.AddHint("No description yet.");
            if (ImGui.Button("Edit description... "))
                EditDescriptionDialog.ShowNextFrame(symbolUi.Symbol);
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
            ImGui.TextUnformatted("Parameter Details");
            ImGui.PopFont();
            ImGui.PopStyleColor();

            var parameterColorWidth = 150f * T3Ui.UiScaleFactor;
            for (var parameterIndex = 0; parameterIndex < _parametersWithDescription.Count; parameterIndex++)
            {
                var p = _parametersWithDescription[parameterIndex];
                ImGui.PushStyleColor(ImGuiCol.Text, UiColors.Text.Rgba);

                var parameterNameWidth = ImGui.CalcTextSize(p.InputDefinition.Name).X;
                ImGui.SetCursorPosX(parameterColorWidth - parameterNameWidth);
                ImGui.TextUnformatted(p.InputDefinition.Name);
                ImGui.PopStyleColor();

                ImGui.SameLine(parameterColorWidth + 10);
                GetParameterDescriptionView(parameterIndex).Draw(p.Description,
                                                                 onOperatorRef: opRefHandler,
                                                                 operatorColor: MarkdownOperatorLinks.GetOperatorColor);
            }
        }

        ImGui.Dummy(Vector2.One);

        DocumentationRenderer.DrawLinksAndExamples(symbolUi);

        ImGui.PopStyleVar();
        ImGui.Unindent(10); // Balance the Indent(10) above; bare Unindent() uses the larger default spacing and leaves a negative indent.
        ImGui.Dummy(new Vector2(10, 10));
        ImGui.PopFont();
    }

    public static class DocumentationRenderer
    {
        private static SymbolUi? _cachedSymbolUi;
        private static Guid _cachedSymbolId;

        private static readonly List<(string Title, string Description, string Url, Icon? Icon)> _cachedLinks = [];
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

            // The symbol links now rank into the unified "watch & learn" list (see GetLinkRows / VideoResourceList),
            // so only the example operators remain here.
            DrawExamples();

            ImGui.PopFont();
            ImGui.PopStyleVar();
        }

        /// <summary>The hand-authored symbol links as rows for the merged help resource list (replaces the old "Links:" section).</summary>
        public static IReadOnlyList<Help.VideoResourceList.LinkRow> GetLinkRows(SymbolUi symbolUi)
        {
            var rows = new List<Help.VideoResourceList.LinkRow>();
            foreach (var link in symbolUi.Links.Values)
            {
                if (string.IsNullOrEmpty(link.Url))
                    continue;

                var title = link.Title ?? link.Type.ToString();
                var icon = ExternalLink.LinkIcons.TryGetValue(link.Type, out var i) ? i : (Icon?)null;
                rows.Add(new Help.VideoResourceList.LinkRow(title, link.Url, link.Description, icon));
            }

            return rows;
        }

        private static void CacheSymbolData(SymbolUi symbolUi)
        {
            _cachedLinks.Clear();

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

    }

    private static void DrawGroupLabel(string title)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        ImGui.TextUnformatted(title);
        ImGui.PopStyleColor();
    }

    internal static readonly EditSymbolDescriptionDialog EditDescriptionDialog = new();

    private static readonly List<IInputUi> _parametersWithDescription = new(10);

    private static readonly MarkdownView _descriptionView = new(new MarkdownView.Options());

    /// <summary>
    /// One view per parameter row — MarkdownView caches a single document, so sharing one
    /// instance across parameters would reparse every description every frame.
    /// Most descriptions were authored as plain text, so newlines stay hard line breaks.
    /// </summary>
    private static readonly List<MarkdownView> _parameterDescriptionViews = new(10);

    private static MarkdownView GetParameterDescriptionView(int parameterIndex)
    {
        while (_parameterDescriptionViews.Count <= parameterIndex)
        {
            _parameterDescriptionViews.Add(new MarkdownView(new MarkdownView.Options
                                                                {
                                                                    HardLineBreaks = true,
                                                                    MutedBodyText = true,
                                                                }));
        }

        return _parameterDescriptionViews[parameterIndex];
    }

    // Cached so the per-frame Draw calls don't allocate new delegates.
    private static readonly MarkdownView.OperatorRefRendered _operatorRefHandler =
        static opName => MarkdownOperatorLinks.HandleOperatorRef(opName);

    private static readonly MarkdownView.OperatorRefRendered _operatorRefHandlerInTooltip =
        static opName => MarkdownOperatorLinks.HandleOperatorRef(opName, suppressTooltip: true);

    private static float _timeSinceTooltipHovered = 0;
    private static readonly string _openLink = "Open link in browser:";
}