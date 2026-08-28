#nullable enable
using System.Threading.Tasks;
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Settings;
using T3.Core.SystemUi;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.Styling.Markdown;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.Layouts;
using T3.Editor.Gui.Windows.TestRunner;

namespace T3.Editor.Gui.Dialog;

/// <summary>
/// Floating, non-modal welcome window for alpha builds, shown the first time a user runs a version
/// they haven't run in this folder before. A Settings-style sidebar splits it into Welcome (intro +
/// release notes), Import Settings, Import Projects, and Test new Features. Reopenable via
/// Help → Welcome. Stable builds show the lighter <see cref="WelcomeWindow"/> instead.
/// </summary>
[HelpUiID("WelcomeAlphaWindow")]
internal sealed class WelcomeAlphaWindow : WelcomeWindowBase
{
    internal WelcomeAlphaWindow()
    {
        Config.Title = "Welcome to the TiXL Alpha";
    }

    protected override void DrawContent()
    {
        if (!_isInitialized)
        {
            Initialize();
            _isInitialized = true;
        }

        // Handle background project-copy completion on the UI thread, regardless of the active tab.
        if (_projectImportTask is { IsCompleted: true })
        {
            _projectImportTask = null;
            _projectsImported = true;
            if (_previous != null)
                _projects = PreviousVersionImport.EnumerateProjects(_previous);
        }

        NavigationSidebar.BeginLayout();
        DrawHeaderBand($"Thanks for testing v{FileLocations.TixlVersion} Alpha");
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
        _settingsImported = false;
        _projectsImported = false;
        _importSettings = _importThemes = _importKeymaps = _importLayouts = true;
        _selectedTestSetId = null;

        _hasPrevious = PreviousVersionImport.TryFindBest(out _previous) && _previous != null;
        if (_previous != null)
        {
            _settingsAvailable = PreviousVersionImport.HasSettingsFile(_previous);
            _themesAvailable = PreviousVersionImport.HasSubfolder(_previous, FileLocations.ThemeSubFolder);
            _keymapsAvailable = PreviousVersionImport.HasSubfolder(_previous, FileLocations.KeyBindingSubFolder);
            _layoutsAvailable = PreviousVersionImport.HasSubfolder(_previous, PreviousVersionImport.LayoutsSubfolder);
            _projects = PreviousVersionImport.EnumerateProjects(_previous);
            foreach (var p in _projects)
                _projectSelection[p.Name] = !p.AlreadyImported;
        }
        else
        {
            _projects = [];
        }

        _testSets = TestSetParser.LoadAll(TestSetParser.ResolveTestsDirectory());
        LoadReleaseNotes();
    }

    private void DrawSidebar()
    {
        NavigationSidebar.BeginColumn("sidebar", 150);

        if (NavigationSidebar.Item("Welcome", _activeTab == Tab.Welcome))
            _activeTab = Tab.Welcome;
        if (NavigationSidebar.Item("Import Settings", _activeTab == Tab.ImportSettings, showCheckmark: _settingsImported))
            _activeTab = Tab.ImportSettings;
        if (NavigationSidebar.Item("Import Projects", _activeTab == Tab.ImportProjects, showCheckmark: _projectsImported))
            _activeTab = Tab.ImportProjects;
        if (NavigationSidebar.Item("Test new Features", _activeTab == Tab.TestFeatures))
            _activeTab = Tab.TestFeatures;

        NavigationSidebar.EndColumn();
    }

    private void DrawContentPanel()
    {
        var title = _activeTab switch
                        {
                            Tab.Welcome        => "Welcome",
                            Tab.ImportSettings => "Import settings",
                            Tab.ImportProjects => "Import Projects",
                            Tab.TestFeatures   => $"Test latest features in v{FileLocations.TixlVersion}",
                            _                  => string.Empty,
                        };

        NavigationSidebar.BeginContentPanel(title, this);

        switch (_activeTab)
        {
            case Tab.Welcome:
                DrawWelcomeTab();
                break;
            case Tab.ImportSettings:
                DrawImportSettingsTab();
                break;
            case Tab.ImportProjects:
                DrawImportProjectsTab();
                break;
            case Tab.TestFeatures:
                DrawTestFeaturesTab();
                break;
        }

        NavigationSidebar.EndContentPanel();
    }

    private void DrawWelcomeTab()
    {
        // Rendered as markdown so the [text](url) links sit inline in the prose.
        _welcomeMarkdown.Draw(AlphaWelcomeMarkdown,
                              onUrl: url => CoreUi.Instance.OpenWithDefaultApplication(url));

        FormInputs.AddVerticalSpace(8);
        ImGui.Separator();

        DrawReleaseNotes();
    }

    private void DrawImportSettingsTab()
    {
        if (!RequirePrevious())
            return;

        ImGui.TextColored(UiColors.TextMuted, $"Copied from \"{_previous!.FolderName}\". The original is left untouched.");
        FormInputs.AddVerticalSpace(6);

        DrawCategoryCheckbox("Editor Settings", ref _importSettings, _settingsAvailable);
        DrawCategoryCheckbox("Themes", ref _importThemes, _themesAvailable);
        DrawCategoryCheckbox("Keyboard Maps", ref _importKeymaps, _keymapsAvailable);
        DrawCategoryCheckbox("Layouts", ref _importLayouts, _layoutsAvailable);

        FormInputs.AddVerticalSpace(8);

        var anySelected = (_importSettings && _settingsAvailable) || (_importThemes && _themesAvailable)
                          || (_importKeymaps && _keymapsAvailable) || (_importLayouts && _layoutsAvailable);

        ImGui.BeginDisabled(!anySelected);
        if (CustomComponents.DrawCtaButton("Import", Icon.None,
                                           anySelected ? UiColors.ForegroundFull : UiColors.TextMuted,
                                           anySelected ? UiColors.StatusActivated : UiColors.BackgroundButton,
                                           Color.Transparent))
        {
            RunSettingsImport(_previous!);
        }

        ImGui.EndDisabled();

        if (_settingsImported)
        {
            ImGui.SameLine();
            ImGui.TextColored(UiColors.StatusActivated, "Imported.");
            FormInputs.AddVerticalSpace(6);
            DrawRestartEditorButton("Restart TiXL to apply the imported settings, themes, keymaps and layouts.");
        }
    }

    private void DrawImportProjectsTab()
    {
        if (!RequirePrevious())
            return;

        ImGui.TextColored(UiColors.TextMuted, $"Copied to {FileLocations.DefaultProjectFolder}.");
        ImGui.TextColored(UiColors.TextMuted, "Projects already present there are skipped.");
        FormInputs.AddVerticalSpace(6);

        if (_projects.Count == 0)
        {
            ImGui.TextColored(UiColors.TextMuted, "No projects found in the previous version.");
            return;
        }

        var scale = T3Ui.UiScaleFactor;
        foreach (var project in _projects)
        {
            ImGui.PushID(project.Name);

            var selected = _projectSelection.TryGetValue(project.Name, out var v) && v;
            ImGui.BeginDisabled(project.AlreadyImported);
            if (ImGui.Checkbox("##sel", ref selected))
                _projectSelection[project.Name] = selected;
            ImGui.EndDisabled();

            ImGui.SameLine();
            var label = project.AlreadyImported ? $"{project.Name}  (already imported)" : project.Name;
            ImGui.TextColored(project.AlreadyImported ? UiColors.TextMuted : UiColors.Text, label);

            ImGui.SameLine();
            CustomComponents.RightAlign(ImGui.GetFrameHeight());
            if (CustomComponents.TransparentIconButton(Icon.FolderOpen, new Vector2(ImGui.GetFrameHeight(), ImGui.GetFrameHeight())))
                CoreUi.Instance.OpenWithDefaultApplication(project.SourcePath);

            ImGui.PopID();
        }

        FormInputs.AddVerticalSpace(8);

        var anySelected = false;
        foreach (var project in _projects)
            anySelected |= !project.AlreadyImported && _projectSelection.TryGetValue(project.Name, out var v) && v;

        ImGui.BeginDisabled(!anySelected || _projectImportTask is { IsCompleted: false });
        if (CustomComponents.DrawCtaButton("Import", Icon.None,
                                           anySelected ? UiColors.ForegroundFull : UiColors.TextMuted,
                                           anySelected ? UiColors.StatusActivated : UiColors.BackgroundButton,
                                           Color.Transparent))
        {
            RunProjectImport();
        }

        ImGui.EndDisabled();

        if (_projectImportTask is { IsCompleted: false })
        {
            ImGui.SameLine();
            ImGui.TextColored(UiColors.StatusAnimated, "Importing…");
        }
        else if (_projectsImported)
        {
            ImGui.SameLine();
            ImGui.TextColored(UiColors.StatusActivated, "Imported.");
            FormInputs.AddVerticalSpace(6);
            DrawRestartEditorButton("Restart TiXL to load the imported projects.");
        }
    }

    private static void DrawRestartEditorButton(string tooltip)
    {
        if (CustomComponents.DrawCtaButton("Restart Editor", Icon.None, UiColors.ForegroundFull, UiColors.StatusActivated, Color.Transparent))
            EditorRestart.TryRestart();
        CustomComponents.TooltipForLastItem(tooltip);
    }

    private void DrawTestFeaturesTab()
    {
        ImGui.TextColored(UiColors.TextMuted, "Walk through recently added features with guided steps.");
        FormInputs.AddVerticalSpace(6);

        var scale = T3Ui.UiScaleFactor;

        // Reserve the footer height so the list scrolls independently and the CTA stays pinned at
        // the bottom (mirrors ManualTestRunnerWindow.DrawPickState).
        var ctaSize = CustomComponents.GetCtaButtonSize("Start Test", Icon.None);
        var footerH = ctaSize.Y + 12 * scale;
        ImGui.BeginChild("setlist", new Vector2(0, -footerH), ImGuiChildFlags.None, ImGuiWindowFlags.NoBackground);
        {
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, 4 * scale));
            if (_testSets.Count == 0)
            {
                ImGui.TextColored(UiColors.TextMuted, "No feature tests found.");
            }
            else
            {
                foreach (var set in _testSets)
                {
                    if (TestSetRow.Draw(set, _selectedTestSetId == set.Id, height: 50).Clicked)
                        _selectedTestSetId = set.Id;
                }
            }

            ImGui.PopStyleVar();
        }
        ImGui.EndChild();

        var hasSelection = _selectedTestSetId != null;
        CustomComponents.RightAlign(ctaSize.X, sameLine: false);
        ImGui.BeginDisabled(!hasSelection);
        if (CustomComponents.DrawCtaButton("Start Test", Icon.None,
                                           hasSelection ? UiColors.ForegroundFull : UiColors.TextMuted,
                                           hasSelection ? UiColors.StatusActivated : UiColors.BackgroundButton,
                                           Color.Transparent))
        {
            WindowManager.GuidedFeatureTestsWindow.StartSet(_selectedTestSetId!);
            Config.Visible = false; // base calls Close() → stamps the version marker
        }

        ImGui.EndDisabled();
    }

    private bool RequirePrevious()
    {
        if (_hasPrevious)
            return true;

        ImGui.TextColored(UiColors.TextMuted, "No previous TiXL version was found to import from.");
        return false;
    }

    private void RunSettingsImport(PreviousVersionImport.PreviousVersion previous)
    {
        if (_importSettings && _settingsAvailable)
            PreviousVersionImport.ImportSettingsAllowlist(previous);
        if (_importThemes && _themesAvailable)
            PreviousVersionImport.ImportSubfolder(previous, FileLocations.ThemeSubFolder);
        if (_importKeymaps && _keymapsAvailable)
            PreviousVersionImport.ImportSubfolder(previous, FileLocations.KeyBindingSubFolder);
        if (_importLayouts && _layoutsAvailable)
            PreviousVersionImport.ImportSubfolder(previous, PreviousVersionImport.LayoutsSubfolder);

        _settingsImported = true;
    }

    private void RunProjectImport()
    {
        var toImport = new List<PreviousVersionImport.ProjectEntry>();
        foreach (var project in _projects)
        {
            if (!project.AlreadyImported && _projectSelection.TryGetValue(project.Name, out var v) && v)
                toImport.Add(project);
        }

        if (toImport.Count == 0)
            return;

        // Copy on a background thread so a large tree doesn't freeze the editor. Completion is handled
        // on the UI thread (see DrawImportProjectsTab) to avoid touching _projects off-thread.
        _projectImportTask = Task.Run(() =>
                                      {
                                          foreach (var project in toImport)
                                          {
                                              try
                                              {
                                                  PreviousVersionImport.ImportProject(project);
                                              }
                                              catch (Exception e)
                                              {
                                                  Log.Warning($"Importing project '{project.Name}' failed: {e.Message}");
                                              }
                                          }
                                      });
    }

    private static void DrawCategoryCheckbox(string label, ref bool value, bool available)
    {
        ImGui.BeginDisabled(!available);
        var shown = value && available;
        if (ImGui.Checkbox(label, ref shown))
            value = shown;
        ImGui.EndDisabled();
        if (!available)
            CustomComponents.TooltipForLastItem($"No {label.ToLowerInvariant()} found in the previous version.");
    }

    private enum Tab
    {
        Welcome,
        ImportSettings,
        ImportProjects,
        TestFeatures,
    }

    private const string AlphaWelcomeMarkdown =
        """
        To avoid affecting already installed versions and your projects, alpha versions always [create their own folders](https://github.com/tixl3d/tixl/wiki) for Settings and Projects. Use the import options on the left to copy data over.

        This version is under [active development](https://github.com/tixl3d/tixl/wiki/dev.UsingDev) — please don't use it for production work.

        Please report all issues on our [Discord server](https://discord.gg/YmSyQdeH3S), or browse the [project planning board](https://github.com/orgs/tixl3d/projects/3/views/8).
        """;

    private readonly MarkdownView _welcomeMarkdown = new(new MarkdownView.Options());

    private bool _isInitialized;
    private Tab _activeTab = Tab.Welcome;

    private bool _hasPrevious;
    private PreviousVersionImport.PreviousVersion? _previous;

    private bool _settingsAvailable;
    private bool _themesAvailable;
    private bool _keymapsAvailable;
    private bool _layoutsAvailable;

    private bool _importSettings = true;
    private bool _importThemes = true;
    private bool _importKeymaps = true;
    private bool _importLayouts = true;
    private bool _settingsImported;

    private IReadOnlyList<PreviousVersionImport.ProjectEntry> _projects = [];
    private readonly Dictionary<string, bool> _projectSelection = new();
    private bool _projectsImported;
    private Task? _projectImportTask;

    private IReadOnlyList<TestSet> _testSets = [];
    private string? _selectedTestSetId;
}
