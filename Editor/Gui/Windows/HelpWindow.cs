#nullable enable
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.SystemUi;
using T3.Editor.Gui.Help;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.Styling.Markdown;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.Windows;

/// <summary>
/// A dockable context-doc panel: it instantly mirrors the operator the user hovers or selects (no dwell
/// delay), rendering its description, parameters, links, and the ranked "Discussed in meet-ups" resources.
/// Pinning detaches the panel from the live selection and pushes the topic onto a back/forward history so
/// the user can keep working in the graph while reading.
/// </summary>
/// <remarks>
/// The doc body reuses <see cref="OperatorHelp.DrawHelp"/>; this window adds the shell (modes, follow/pin
/// state machine) and the meet-up resource list. See issue #102.
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
    }

    internal override List<Window> GetInstances() => [];

    protected override void DrawContent()
    {
        // Resolve the follow target before the header so the pin button reflects whether there's anything to pin.
        if (_mode == Modes.Help && !_isPinned)
            _followingSymbolId = ResolveFollowTarget();

        DrawHeader();
        CustomComponents.SeparatorLine();

        if (_mode == Modes.Help)
            DrawHelpBody();
        else
            DrawLearnBody();
    }

    #region header
    private void DrawHeader()
    {
        ImGui.AlignTextToFramePadding();

        DrawModeTab("Help", Modes.Help);
        ImGui.SameLine();
        DrawModeTab("Learn", Modes.Learn);

        // Pin + history controls only make sense in Help mode.
        if (_mode != Modes.Help)
            return;

        var buttonSize = new Vector2(ImGui.GetFrameHeight());
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var buttonCount = _isPinned ? 4 : 3;
        var clusterWidth = buttonCount * buttonSize.X + (buttonCount - 1) * spacing;
        CustomComponents.RightAlign(clusterWidth);

        var canGoBack = _historyIndex > 0;
        if (CustomComponents.IconButton(Icon.ChevronLeft, buttonSize,
                                        canGoBack ? CustomComponents.ButtonStates.Default : CustomComponents.ButtonStates.Disabled)
            && canGoBack)
        {
            StepHistory(-1);
        }

        CustomComponents.TooltipForLastItem("Previous topic");

        ImGui.SameLine();
        var canGoForward = _historyIndex >= 0 && _historyIndex < _history.Count - 1;
        if (CustomComponents.IconButton(Icon.ChevronRight, buttonSize,
                                        canGoForward ? CustomComponents.ButtonStates.Default : CustomComponents.ButtonStates.Disabled)
            && canGoForward)
        {
            StepHistory(1);
        }

        CustomComponents.TooltipForLastItem("Next topic");

        ImGui.SameLine();
        var hasTarget = _followingSymbolId != Guid.Empty || _isPinned;
        var pinState = _isPinned
                           ? CustomComponents.ButtonStates.Activated
                           : !hasTarget
                               ? CustomComponents.ButtonStates.Disabled
                               // Brighten the pin until the user discovers it; retire the hint after the first pin.
                               : _pinHintRetired
                                   ? CustomComponents.ButtonStates.Default
                                   : CustomComponents.ButtonStates.Emphasized;

        if (CustomComponents.IconButton(_isPinned ? Icon.Pin : Icon.PinOutline, buttonSize, pinState) && hasTarget)
            TogglePin();

        CustomComponents.TooltipForLastItem(_isPinned
                                                ? "Unpin — follow the selection again"
                                                : "Pin this topic so it stays while you keep exploring the graph");

        if (_isPinned)
        {
            ImGui.SameLine();
            if (CustomComponents.IconButton(Icon.Close, buttonSize))
                Unpin();

            CustomComponents.TooltipForLastItem("Unpin");
        }
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

    #region help mode
    private void DrawHelpBody()
    {
        var showId = _isPinned && _historyIndex >= 0 && _historyIndex < _history.Count
                         ? _history[_historyIndex]
                         : _followingSymbolId;

        var topicChanged = showId != _lastShownSymbolId;
        if (topicChanged)
        {
            _resourceList.Reset();
            _lastShownSymbolId = showId;
        }

        SymbolUi? symbolUi = null;
        string? operatorFullPath = null;
        if (showId != Guid.Empty && SymbolUiRegistry.TryGetSymbolUi(showId, out symbolUi))
            operatorFullPath = symbolUi.Symbol.Namespace + "." + symbolUi.Symbol.Name;

        // The hand-authored symbol links rank into the same footer list as the extracted video references.
        var resourceLinks = symbolUi != null
                                ? OperatorHelp.DocumentationRenderer.GetLinkRows(symbolUi)
                                : (IReadOnlyList<VideoResourceList.LinkRow>)Array.Empty<VideoResourceList.LinkRow>();

        // Reserve space at the bottom so the resources stay docked while the doc scrolls above them.
        var bodyHeight = ImGui.GetContentRegionAvail().Y;
        var footerHeight = symbolUi != null ? _resourceList.MeasureHeight(operatorFullPath, resourceLinks, bodyHeight) : 0f;

        // The doc always lives in a child: the empty-state message draws straight to the draw list and
        // submits no item, so without the child's own item the dangling cursor from the header separator
        // would trip ImGui's "validate extent" assert at the window's EndChild.
        ImGui.BeginChild("doc", new Vector2(0, -footerHeight), ImGuiChildFlags.None, ImGuiWindowFlags.NoBackground);
        if (topicChanged)
            ImGui.SetScrollY(0);

        if (symbolUi != null)
        {
            OperatorHelp.DrawHelp(symbolUi);
        }
        else
        {
            CustomComponents.EmptyWindowMessage(_isPinned
                                                    ? "This pinned topic is no longer available."
                                                    : "Hover or select an operator to see its description.");
        }

        CustomComponents.HandleDragScrolling(this);
        ImGui.EndChild();

        if (footerHeight > 0)
            DrawResourceFooter(operatorFullPath, resourceLinks);
    }

    private void DrawResourceFooter(string? operatorFullPath, IReadOnlyList<VideoResourceList.LinkRow> links)
    {
        // Subtle divider at the docked footer's top edge.
        var top = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddLine(top, top + new Vector2(ImGui.GetContentRegionAvail().X, 0),
                                          UiColors.ForegroundFull.Fade(0.1f));

        ImGui.BeginChild("resources", Vector2.Zero, ImGuiChildFlags.None, ImGuiWindowFlags.NoBackground);
        ImGui.Indent(10); // Match the inset OperatorHelp.DrawHelp uses for the doc body.
        _resourceList.Draw(operatorFullPath, links);
        ImGui.Unindent(10);
        ImGui.EndChild();
    }

    private static Guid ResolveFollowTarget()
    {
        // Hovering a node beats the current selection so scrubbing the graph updates the panel instantly.
        if (HoveredHelpTarget.TryGetOperator(out var hoveredSymbolId))
            return hoveredSymbolId;

        if (NodeSelection.TryGetSelectedInstanceOrInput(out var instance, out _, out _))
            return instance.GetSymbolUi().Symbol.Id;

        return Guid.Empty;
    }

    private void TogglePin()
    {
        if (_isPinned)
        {
            Unpin();
            return;
        }

        if (_followingSymbolId == Guid.Empty)
            return;

        PushHistory(_followingSymbolId);
        _isPinned = true;
        _pinHintRetired = true;
    }

    private void Unpin()
    {
        _isPinned = false;
    }

    private void PushHistory(Guid symbolId)
    {
        // Already on this topic at the head: just re-pin it without duplicating the entry.
        if (_historyIndex >= 0 && _historyIndex < _history.Count && _history[_historyIndex] == symbolId)
            return;

        // Drop any forward entries — pinning a new topic starts a fresh forward trail.
        if (_historyIndex < _history.Count - 1)
            _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);

        _history.Add(symbolId);
        _historyIndex = _history.Count - 1;
    }

    private void StepHistory(int direction)
    {
        _historyIndex = Math.Clamp(_historyIndex + direction, 0, _history.Count - 1);
        _isPinned = true; // Landing on a history entry pins it.
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

    private Modes _mode = Modes.Help;

    private bool _isPinned;
    private bool _pinHintRetired;
    private Guid _followingSymbolId;
    private Guid _lastShownSymbolId;

    private readonly List<Guid> _history = new();
    private int _historyIndex = -1;

    private readonly VideoResourceList _resourceList = new();
    private readonly MarkdownView _releaseNotesView = new(new MarkdownView.Options());
}
