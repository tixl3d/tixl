#nullable enable
using ImGuiNET;
using T3.Core.SystemUi;
using T3.Editor.Gui.Help;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.Styling.Markdown;
using T3.Editor.Gui.UiHelpers;
// `Window` (singular) is also a sibling namespace, so the base class is referenced by its alias.
using WindowBase = T3.Editor.Gui.Windows.Window;

namespace T3.Editor.Gui.Dialog;

/// <summary>
/// Shared chrome for the version-welcome windows — the stable <see cref="WelcomeWindow"/> and the
/// <see cref="WelcomeAlphaWindow"/>. Provides the floating Settings-style window shape, the header
/// band with the version heading and the "Show on startup" toggle, the release-notes rendering,
/// and version-marker stamping on close.
/// </summary>
internal abstract class WelcomeWindowBase : WindowBase
{
    internal bool IsVisible => Config.Visible;

    /// <summary>Opens the window (re-initialising its tabs on the next draw). Used by the version-welcome trigger and Help → Welcome.</summary>
    internal void Open() => Config.Visible = true;

    internal override IReadOnlyList<WindowBase> GetInstances() => Array.Empty<WindowBase>();

    private protected WelcomeWindowBase()
    {
        // No window padding so the sidebar sits flush like the Settings window; the header and the
        // content panel add their own inner padding. The base supplies the Settings-matching chrome.
        WindowPaddingOverride = Vector2.Zero;
        WindowSizeOverride = new Vector2(680, 480);
        WindowFlags = ImGuiWindowFlags.NoDocking;
        CenterOnAppearing = true;
    }

    protected override void Close()
    {
        VersionMarker.MarkCurrentVersionSeen();
    }

    protected void DrawHeaderBand(string heading)
    {
        var scale = T3Ui.UiScaleFactor;

        // Window padding is 0, so pad the header explicitly.
        ImGui.Dummy(new Vector2(0, 8 * scale));
        ImGui.Indent(12 * scale);
        ImGui.PushFont(Fonts.FontLarge);
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        ImGui.TextUnformatted(heading);
        ImGui.PopStyleColor();
        ImGui.PopFont();

        // "Show on startup" toggle, right-aligned on the heading row.
        DrawShowOnStartupCheckbox();

        ImGui.Unindent(12 * scale);

        ImGui.Dummy(new Vector2(0, 6 * scale));
        ImGui.Separator();
    }

    protected void LoadReleaseNotes()
    {
        _releaseNotes = ReleaseNotesLoader.TryLoadForCurrentVersion();
    }

    protected void DrawReleaseNotes()
    {
        if (_releaseNotes != null)
        {
            // Rendered as markdown so [text](url) links and [OperatorName] references are both
            // interactive — the latter open the Symbol Library on click.
            _releaseNotesMarkdown.Draw(_releaseNotes,
                                       onUrl: url => CoreUi.Instance.OpenWithDefaultApplication(url),
                                       onOperatorRef: opName => MarkdownOperatorLinks.HandleOperatorRef(opName),
                                       operatorColor: MarkdownOperatorLinks.GetOperatorColor);
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
            ImGui.TextWrapped("No release notes for this version yet.");
            ImGui.PopStyleColor();
        }
    }

    private static void DrawShowOnStartupCheckbox()
    {
        var scale = T3Ui.UiScaleFactor;
        const string label = "Show on startup";

        var width = ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X + ImGui.CalcTextSize(label).X;
        CustomComponents.RightAlign(width + 14 * scale);

        // Vertically center the normal-height checkbox against the FontLarge heading on this row.
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (Fonts.FontLarge.FontSize - ImGui.GetFrameHeight()) * 0.5f);

        var show = UserSettings.Config.ShowWelcomeOnStartup;
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        if (ImGui.Checkbox(label, ref show))
        {
            UserSettings.Config.ShowWelcomeOnStartup = show;
            UserSettings.Save();
        }

        ImGui.PopStyleColor();
        CustomComponents.TooltipForLastItem("When off, the window no longer opens at launch — reopen it any time from Help → Welcome.");
    }

    private readonly MarkdownView _releaseNotesMarkdown = new(new MarkdownView.Options());
    private string? _releaseNotes;
}
