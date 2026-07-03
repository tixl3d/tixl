#nullable enable
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.SystemUi;
using T3.Editor.Gui.Help;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.Styling.Markdown;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.UiHelpers.Thumbnails;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.Windows;

/// <summary>
/// A dockable context-doc panel: it shows the documentation of the current help context — the last
/// explicitly selected operator or <c>ui:</c> topic — rendering its description, parameters, links, and
/// the ranked "Discussed in meet-ups" resources. While the hover toggle is on, hovering an operator or a
/// documentation link previews its doc instantly (no dwell delay) and reverts to the context afterwards.
/// Every context change is pushed onto a deduplicated back/forward history.
/// </summary>
/// <remarks>
/// The operator doc body reuses <see cref="OperatorHelp.DrawHelp"/>; this window adds the shell (modes,
/// context/history state) and the meet-up resource list. See issue #102.
/// </remarks>
[HelpUiID("HelpWindow")]
internal sealed class HelpWindow : Window
{
    public HelpWindow()
    {
        Config.Title = "Help";
        MenuTitle = "Help";
        WindowFlags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
        WindowSizeOverride = new Vector2(420, 600);
        _instance = this;
    }

    internal override List<Window> GetInstances() => [];

    /// <summary>
    /// Makes <paramref name="topic"/> the current help context (pushed onto the history). Called by
    /// documentation links, doc icons, and the symbol library; pass <paramref name="showWindow"/> for
    /// clicks that should surface the window even when it is closed.
    /// </summary>
    internal static void ShowTopic(in HelpTopic topic, bool showWindow = false)
    {
        var instance = _instance;
        if (instance == null || topic.IsEmpty)
            return;

        instance.PushTopic(topic);

        if (showWindow)
        {
            instance.Config.Visible = true;
            instance._mode = Modes.Help;
            instance._focusWindowNextFrame = true;
        }
    }

    /// <summary>
    /// True while the Help window is on screen in Help mode with the hover toggle enabled. Views that
    /// show their own doc tooltips (symbol library/browser, topic links) omit them while this is set,
    /// since the window already previews the hovered topic.
    /// </summary>
    internal static bool HoverPreviewActive
    {
        get
        {
            var instance = _instance;
            return instance != null
                   && instance._mode == Modes.Help
                   && UserSettings.Config.HelpHoverPreview
                   // Frame-stamped instead of Config.Visible so a hidden dock tab doesn't suppress tooltips.
                   && ImGui.GetFrameCount() - instance._lastContentFrame <= 1;
        }
    }

    /// <summary>
    /// True while this window draws its own content. Doc links check it so hovering a link *inside* the
    /// Help window keeps its tooltip instead of hover-previewing — swapping the body under the cursor
    /// would remove the hovered link and flicker between the two topics. Clicking still navigates.
    /// </summary>
    internal static bool IsDrawingContent { get; private set; }

    protected override void DrawContent()
    {
        IsDrawingContent = true;
        _lastContentFrame = ImGui.GetFrameCount();

        if (_focusWindowNextFrame)
        {
            ImGui.SetWindowFocus();
            _focusWindowNextFrame = false;
        }

        UpdateContextFromGraphSelection();

        DrawHeader();
        CustomComponents.SeparatorLine();

        if (_mode == Modes.Help)
            DrawHelpBody();
        else
            DrawLearnBody();

        IsDrawingContent = false;
    }

    #region header
    private void DrawHeader()
    {
        ImGui.AlignTextToFramePadding();

        DrawModeTab("Help", Modes.Help);
        ImGui.SameLine();
        DrawModeTab("Learn", Modes.Learn);

        // History + hover controls only make sense in Help mode.
        if (_mode != Modes.Help)
            return;

        var buttonSize = new Vector2(ImGui.GetFrameHeight());
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        CustomComponents.RightAlign(3 * buttonSize.X + 2 * spacing);

        // Index 0 is the newest entry, so "back" moves towards higher indices (see PushTopic).
        var canGoBack = _historyIndex >= 0 && _historyIndex < _history.Count - 1;
        if (CustomComponents.IconButton(Icon.ChevronLeft, buttonSize,
                                        canGoBack ? CustomComponents.ButtonStates.Default : CustomComponents.ButtonStates.Disabled)
            && canGoBack)
        {
            _historyIndex++;
        }

        CustomComponents.TooltipForLastItem("Previous topic");

        ImGui.SameLine();
        var canGoForward = _historyIndex > 0;
        if (CustomComponents.IconButton(Icon.ChevronRight, buttonSize,
                                        canGoForward ? CustomComponents.ButtonStates.Default : CustomComponents.ButtonStates.Disabled)
            && canGoForward)
        {
            _historyIndex--;
        }

        CustomComponents.TooltipForLastItem("Next topic");

        ImGui.SameLine();
        CustomComponents.ToggleTwoIconsButton(ref UserSettings.Config.HelpHoverPreview,
                                              iconOff: Icon.HoverScrub,
                                              iconOn: Icon.HoverScrub,
                                              CustomComponents.ButtonStates.Activated,
                                              CustomComponents.ButtonStates.Default,
                                              noBackground: true);

        CustomComponents.TooltipForLastItem("Preview on hover",
                                            "Hovering an operator or a documentation link shows its doc here instantly.");
    }

    private void DrawModeTab(string label, Modes mode)
    {
        var isActive = _mode == mode;
        ImGui.PushStyleColor(ImGuiCol.Text, (isActive ? UiColors.Text : UiColors.TextMuted).Rgba);
        ImGui.PushFont(isActive ? Fonts.FontBold : Fonts.FontNormal);

        var size = new Vector2(ImGui.CalcTextSize(label).X, ImGui.GetFrameHeight());
        if (ImGui.Selectable(label, isActive, ImGuiSelectableFlags.None, size))
            _mode = mode;

        ImGui.PopFont();
        ImGui.PopStyleColor();
    }
    #endregion

    #region context & history
    /// <summary>A changed graph selection is an explicit context change; clearing the selection keeps the topic.</summary>
    private void UpdateContextFromGraphSelection()
    {
        if (NodeSelection.TryGetSelectedInstanceOrInput(out var instance, out _, out _))
        {
            var symbolId = instance.GetSymbolUi().Symbol.Id;
            if (symbolId != _lastGraphSelectionId)
            {
                _lastGraphSelectionId = symbolId;
                PushTopic(HelpTopic.ForOperator(symbolId));
            }
        }
        else
        {
            _lastGraphSelectionId = Guid.Empty;
        }
    }

    /// <summary>
    /// Deduplicating history update mirroring <see cref="UiModel.ProjectHandling.NavigationHistory"/>:
    /// newest at index 0; re-selecting a neighbor of the current entry keeps the list order, anything
    /// further away is moved to the front. Stepping back through the history and then selecting something
    /// new jumps back to the front.
    /// </summary>
    private void PushTopic(in HelpTopic topic)
    {
        var index = _history.IndexOf(topic);
        if (index == -1)
        {
            _history.Insert(0, topic);
            _historyIndex = 0;
            return;
        }

        if (Math.Abs(index - _historyIndex) <= 1)
        {
            _historyIndex = index;
            return;
        }

        _history.RemoveAt(index);
        _history.Insert(0, topic);
        _historyIndex = 0;
    }

    /// <summary>The hovered topic previews over the current context; it never enters the history.</summary>
    private HelpTopic GetTopicToShow()
    {
        if (UserSettings.Config.HelpHoverPreview && HoveredHelpTarget.TryGetHovered(out var hovered))
            return hovered;

        return _historyIndex >= 0 && _historyIndex < _history.Count
                   ? _history[_historyIndex]
                   : default;
    }
    #endregion

    #region help mode
    private void DrawHelpBody()
    {
        var topic = GetTopicToShow();
        var topicChanged = topic != _lastShownTopic;
        if (topicChanged)
        {
            _resourceList.Reset();
            _lastShownTopic = topic;
        }

        SymbolUi? symbolUi = null;
        string? topicTitle = null;
        string? topicDoc = null;
        string? mentionKey = null;
        var resourceLinks = (IReadOnlyList<VideoResourceList.LinkRow>)Array.Empty<VideoResourceList.LinkRow>();

        if (topic.UiTopicKey != null)
        {
            if (UiTopicDocs.TryGet(topic.UiTopicKey, out var title, out topicDoc))
            {
                topicTitle = title;
                mentionKey = topic.UiTopicKey;
            }
        }
        else if (topic.SymbolId != Guid.Empty && SymbolUiRegistry.TryGetSymbolUi(topic.SymbolId, out symbolUi))
        {
            mentionKey = "op:" + symbolUi.Symbol.Namespace + "." + symbolUi.Symbol.Name;
            // The hand-authored symbol links rank into the same footer list as the extracted video references.
            resourceLinks = OperatorHelp.DocumentationRenderer.GetLinkRows(symbolUi);
        }

        // Reserve space at the bottom so the resources stay docked while the doc scrolls above them.
        var bodyHeight = ImGui.GetContentRegionAvail().Y;
        var footerHeight = mentionKey != null ? _resourceList.MeasureHeight(mentionKey, resourceLinks, bodyHeight) : 0f;

        // The doc always lives in a child: the empty-state message draws straight to the draw list and
        // submits no item, so without the child's own item the dangling cursor from the header separator
        // would trip ImGui's "validate extent" assert at the window's EndChild.
        ImGui.BeginChild("doc", new Vector2(0, -footerHeight), ImGuiChildFlags.None, ImGuiWindowFlags.NoBackground);
        if (topicChanged)
            ImGui.SetScrollY(0);

        if (symbolUi != null)
        {
            DrawSymbolThumbnail(symbolUi);
            OperatorHelp.DrawHelp(symbolUi);
        }
        else if (topicTitle != null)
        {
            DrawUiTopicDoc(topicTitle, topicDoc);
        }
        else if (!topic.IsEmpty)
        {
            CustomComponents.EmptyWindowMessage("This topic is no longer available.");
        }
        else
        {
            CustomComponents.EmptyWindowMessage(UserSettings.Config.HelpHoverPreview
                                                    ? "Hover or select an operator to see its description."
                                                    : "Select an operator to see its description.");
        }

        CustomComponents.HandleDragScrolling(this);
        ImGui.EndChild();

        if (footerHeight > 0)
            DrawResourceFooter(mentionKey, resourceLinks);
    }

    private static void DrawSymbolThumbnail(SymbolUi symbolUi)
    {
        var symbol = symbolUi.Symbol;
        var thumbnail = ThumbnailManager.GetThumbnail(symbol.Id, symbol.SymbolPackage, ThumbnailManager.Categories.PackageMeta);
        if (!thumbnail.IsReady)
            return;

        ImGui.Indent(10); // Match the inset OperatorHelp.DrawHelp uses for the doc body.
        thumbnail.AsImguiImage(100 * T3Ui.UiScaleFactor);
        ImGui.Unindent(10);
        FormInputs.AddVerticalSpace(4);
    }

    private void DrawUiTopicDoc(string title, string? doc)
    {
        ImGui.Indent(10); // Match the inset OperatorHelp.DrawHelp uses for the doc body.
        FormInputs.AddVerticalSpace(10);
        CustomComponents.StylizedText(title, Fonts.FontLarge, UiColors.Text);
        CustomComponents.StylizedText("UI topic", Fonts.FontSmall, UiColors.TextMuted);
        FormInputs.AddVerticalSpace(10);

        if (!string.IsNullOrWhiteSpace(doc))
        {
            _topicView.Draw(doc,
                            onUrl: static url => CoreUi.Instance.OpenWithDefaultApplication(url),
                            onOperatorRef: static op => MarkdownOperatorLinks.HandleOperatorRef(op),
                            operatorColor: MarkdownOperatorLinks.GetOperatorColor);
        }
        else
        {
            CustomComponents.StylizedText("No documentation for this topic yet.", Fonts.FontNormal, UiColors.TextMuted);
        }

        ImGui.Unindent(10);
    }

    private void DrawResourceFooter(string? mentionKey, IReadOnlyList<VideoResourceList.LinkRow> links)
    {
        // Subtle divider at the docked footer's top edge.
        var top = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddLine(top, top + new Vector2(ImGui.GetContentRegionAvail().X, 0),
                                          UiColors.ForegroundFull.Fade(0.1f));

        ImGui.BeginChild("resources", Vector2.Zero, ImGuiChildFlags.None, ImGuiWindowFlags.NoBackground);
        ImGui.Indent(10); // Match the inset OperatorHelp.DrawHelp uses for the doc body.
        _resourceList.Draw(mentionKey, links);
        ImGui.Unindent(10);
        ImGui.EndChild();
    }
    #endregion

    #region learn mode
    private void DrawLearnBody()
    {
        var notes = ReleaseNotesLoader.TryLoadForCurrentVersion();

        ImGui.BeginChild("learnBody", Vector2.Zero, ImGuiChildFlags.None, ImGuiWindowFlags.NoBackground);
        if (string.IsNullOrEmpty(notes))
        {
            CustomComponents.EmptyWindowMessage("No release notes for this version yet.");
        }
        else
        {
            _releaseNotesView.Draw(notes,
                                   onUrl: static url => CoreUi.Instance.OpenWithDefaultApplication(url),
                                   onOperatorRef: static op => MarkdownOperatorLinks.HandleOperatorRef(op),
                                   operatorColor: MarkdownOperatorLinks.GetOperatorColor);
        }

        CustomComponents.HandleDragScrolling(this);
        ImGui.EndChild();
    }
    #endregion

    private enum Modes
    {
        Help,
        Learn,
    }

    private static HelpWindow? _instance;

    private Modes _mode = Modes.Help;
    private bool _focusWindowNextFrame;
    private int _lastContentFrame = int.MinValue;

    private Guid _lastGraphSelectionId;
    private HelpTopic _lastShownTopic;

    private readonly List<HelpTopic> _history = new();
    private int _historyIndex = -1;

    private readonly VideoResourceList _resourceList = new();
    private readonly MarkdownView _topicView = new(new MarkdownView.Options());
    private readonly MarkdownView _releaseNotesView = new(new MarkdownView.Options());
}
