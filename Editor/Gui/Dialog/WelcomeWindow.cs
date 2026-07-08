#nullable enable
using System.IO;
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Settings;
using T3.Core.SystemUi;
using T3.Editor.Gui.AutoBackup;
using T3.Editor.Gui.Hub;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.Styling.Markdown;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Window;
using T3.Editor.UiModel;

namespace T3.Editor.Gui.Dialog;

/// <summary>
/// Floating, non-modal welcome window for stable builds, shown the first time a user runs a version
/// they haven't run in this folder before. A Settings-style sidebar splits it into Welcome (intro +
/// release notes), Getting Started (learn links), and Projects (jump back into a project).
/// Reopenable via Help → Welcome. Alpha builds show the <see cref="WelcomeAlphaWindow"/> instead.
/// </summary>
[HelpUiID("WelcomeWindow")]
internal sealed class WelcomeWindow : WelcomeWindowBase
{
    internal WelcomeWindow()
    {
        Config.Title = "Welcome to TiXL";
    }

    protected override void DrawContent()
    {
        if (!_isInitialized)
        {
            Initialize();
            _isInitialized = true;
        }

        NavigationSidebar.BeginLayout();
        DrawHeaderBand($"Welcome to TiXL v{FileLocations.TixlVersion}");
        DrawSidebar();
        DrawContentPanel();
    }

    protected override void Close()
    {
        base.Close();
        _isInitialized = false;
    }

    private void Initialize()
    {
        _activeTab = Tab.Welcome;
        LoadReleaseNotes();
    }

    private void DrawSidebar()
    {
        NavigationSidebar.BeginColumn("sidebar", 150);

        if (NavigationSidebar.Item("Welcome", _activeTab == Tab.Welcome))
            _activeTab = Tab.Welcome;
        if (NavigationSidebar.Item("Getting Started", _activeTab == Tab.GettingStarted))
            _activeTab = Tab.GettingStarted;
        if (NavigationSidebar.Item("Release Notes", _activeTab == Tab.ReleaseNotes))
            _activeTab = Tab.ReleaseNotes;

        NavigationSidebar.EndColumn();
    }

    private void DrawContentPanel()
    {
        var title = _activeTab switch
                        {
                            Tab.Welcome        => "Welcome",
                            Tab.GettingStarted => "Getting Started",
                            Tab.ReleaseNotes       => "Release Notes",
                            _                  => string.Empty,
                        };

        NavigationSidebar.BeginContentPanel(title, this);

        switch (_activeTab)
        {
            case Tab.Welcome:
                DrawWelcomeTab();
                break;
            case Tab.GettingStarted:
                DrawGettingStartedTab();
                break;
            case Tab.ReleaseNotes:
                DrawReleaseNotes();
                break;
        }

        NavigationSidebar.EndContentPanel();
    }

    private void DrawWelcomeTab()
    {
        DrawPreUpgradeBackupNotice();

        // Rendered as markdown so the [text](url) links sit inline in the prose.
        _welcomeMarkdown.Draw(WelcomeMarkdown,
                              onUrl: url => CoreUi.Instance.OpenWithDefaultApplication(url));

        FormInputs.AddVerticalSpace(8);
        ImGui.Separator();

        //DrawReleaseNotes();
    }

    /// <summary>
    /// One-time notice shown after a launch that snapshotted projects before a breaking file-format
    /// change (see <see cref="PreUpgradeSnapshot"/>). Lists each backup's absolute path so the user
    /// can revert to an earlier TiXL version without losing work.
    /// </summary>
    private static void DrawPreUpgradeBackupNotice()
    {
        var created = PreUpgradeSnapshot.Created;
        if (created.Count == 0)
            return;

        CustomComponents.StylizedText($"{created.Count} project(s) were backed up before the format change",
                                      Fonts.FontBold, UiColors.StatusAttention);
        ImGui.TextWrapped($"TiXL v{FileLocations.TixlVersion} introduces a new project file format. A backup of each "
                          + "project's source and symbol files was saved first, so you can return to an earlier "
                          + "TiXL version without losing work.");

        FormInputs.AddVerticalSpace(4);
        foreach (var entry in created)
        {
            var backupFolder = Path.GetDirectoryName(entry.ZipPath) ?? entry.ZipPath;
            ImGui.PushID(entry.ProjectName);
            if (ImGui.SmallButton("Reveal"))
                CoreUi.Instance.OpenWithDefaultApplication(backupFolder);
            ImGui.SameLine();
            ImGui.TextColored(UiColors.TextMuted, $"{entry.ProjectName}  —  {entry.ZipPath}");
            ImGui.PopID();
        }

        FormInputs.AddVerticalSpace(8);
        ImGui.Separator();
        FormInputs.AddVerticalSpace(8);
    }

    private void DrawGettingStartedTab()
    {
        _gettingStartedMarkdown.Draw(GettingStartedMarkdown,
                                     onUrl: url => CoreUi.Instance.OpenWithDefaultApplication(url));
    }
    
    private enum Tab
    {
        Welcome,
        GettingStarted,
        ReleaseNotes,
    }

    private const string WelcomeMarkdown =
        """
        TiXL is a free, open-source editor for creating real-time motion graphics.

        If you are new, the **Getting Started** page on the left collects the best tutorials and links. The **Projects** page lists your projects so you can jump right back in.

        Questions, feedback, and show-and-tell are always welcome on our [Discord server](https://discord.gg/YmSyQdeH3S).
        """;

    private const string GettingStartedMarkdown =
        """
        ## Learn the basics

        - [Getting Started video (15 min)](https://www.youtube.com/watch?v=_zvzX0fZ8sc)
        - [Tutorials playlist](https://www.youtube.com/playlist?list=PLj-rnPROvbn3LigXGRSDvmLtgTwmNHcQs)
        - [Written introduction](https://github.com/tixl3d/tixl/wiki/help.Introduction)
        - [FAQ](https://github.com/tixl3d/tixl/wiki/help.FAQ)

        Prefer learning inside the editor? Close this window and follow the **Skill Quest** shown while no project is open — an interactive journey from playful basics to advanced real-time graphics.

        ## Community

        - [Discord community](https://discord.com/invite/YmSyQdeH3S) — ask questions, learn from each other, share your work or just hang out
        - [Made with TiXL](https://www.youtube.com/playlist?list=PLj-rnPROvbn3LNU34daaRk5EiaXlwo2E0) — a playlist of work made by the community
        """;

    private readonly MarkdownView _welcomeMarkdown = new(new MarkdownView.Options());
    private readonly MarkdownView _gettingStartedMarkdown = new(new MarkdownView.Options());

    private bool _isInitialized;
    private Tab _activeTab = Tab.Welcome;
}
