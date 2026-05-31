#nullable enable
using System.Diagnostics;
using System.Threading.Tasks;
using ImGuiNET;
using T3.Core.Compilation;
using T3.Core.DataTypes.Vector;
using T3.Core.Settings;
using T3.Core.SystemUi;
using T3.Editor.Gui.Help;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.Styling.Markdown;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.Layouts;
using T3.Editor.Gui.Windows.TestRunner;
// `Window` (singular) is also a sibling namespace, so the base class is referenced by its alias.
using WindowBase = T3.Editor.Gui.Windows.Window;

namespace T3.Editor.Gui.Dialog;

/// <summary>
/// Floating, non-modal welcome window shown the first time a user runs a version they haven't run in
/// this folder before. A Settings-style sidebar splits it into Welcome (intro + release notes),
/// Import Settings, Import Projects, and Test new Features. Reopenable via Help → Welcome.
/// Currently serves both the alpha and the stable variant; named with the <c>Alpha</c> suffix to leave
/// room for a future lighter-weight <c>WelcomeWindow</c> for the stable "what's new in this version" case.
/// </summary>
internal sealed class WelcomeAlphaWindow : WindowBase
{
    internal WelcomeAlphaWindow()
    {
        _isAlpha = RuntimeAssemblies.IsAlpha;
        Config.Title = _isAlpha ? "Welcome to the TiXL Alpha" : "Welcome to TiXL";
        // No window padding so the sidebar sits flush like the Settings window; the header and the
        // content panel add their own inner padding. The base supplies the Settings-matching chrome.
        WindowPaddingOverride = Vector2.Zero;
        WindowSizeOverride = new Vector2(680, 480);
        WindowFlags = ImGuiWindowFlags.NoDocking;
    }

    internal bool IsVisible => Config.Visible;

    /// <summary>Opens the window (re-initialising its tabs). Used by the version-welcome trigger and Help → Welcome.</summary>
    internal void Open() => Config.Visible = true;

    internal override IReadOnlyList<WindowBase> GetInstances() => Array.Empty<WindowBase>();

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

        DrawHeaderBand();
        DrawSidebar();
        ImGui.SameLine(0, 0);
        DrawContentPanel();
    }

    protected override void Close()
    {
        VersionMarker.MarkCurrentVersionSeen();
        _isInitialized = false;
    }

    private void Initialize()
    {
        _isAlpha = RuntimeAssemblies.IsAlpha;
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
        _releaseNotes = ReleaseNotesLoader.TryLoadForCurrentVersion();
    }

    private void DrawHeaderBand()
    {
        var scale = T3Ui.UiScaleFactor;
        var shortVersion = FileLocations.TixlVersion;
        var heading = _isAlpha ? $"Thanks for testing v{shortVersion} Alpha" : $"Welcome to TiXL v{shortVersion}";

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
        // Match the Settings window's content panel (padding + border + see-through background).
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(20, 5) * T3Ui.UiScaleFactor);
        ImGui.BeginChild("content", new Vector2(0, 0), ImGuiChildFlags.Borders | ImGuiChildFlags.AlwaysUseWindowPadding, ImGuiWindowFlags.NoBackground);

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

        ImGui.EndChild();
        ImGui.PopStyleVar();
    }

    private void DrawWelcomeTab()
    {
        FormInputs.AddSectionHeader("Welcome");

        // Rendered as markdown so the [text](url) links sit inline in the prose.
        _welcomeMarkdown.Draw(_isAlpha ? AlphaWelcomeMarkdown : StableWelcomeMarkdown,
                              onUrl: url => CoreUi.Instance.OpenWithDefaultApplication(url));

        FormInputs.AddVerticalSpace(8);
        ImGui.Separator();
        //FormInputs.AddSectionHeader(_isAlpha ? $"Release Notes v{FileLocations.TixlVersion} (WIP)" : $"Release Notes v{FileLocations.TixlVersion}");

        if (_releaseNotes != null)
        {
            // Same renderer as the welcome prose, so [text](url) links and [OperatorName] references
            // are both interactive — the latter open the Symbol Library on click.
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

    private void DrawImportSettingsTab()
    {
        FormInputs.AddSectionHeader("Import settings");
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
        FormInputs.AddSectionHeader("Import Projects");
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

    private static void DrawRestartEditorButton(string tooltip)
    {
        if (CustomComponents.DrawCtaButton("Restart Editor", Icon.None, UiColors.ForegroundFull, UiColors.StatusActivated, Color.Transparent))
            RestartEditor();
        CustomComponents.TooltipForLastItem(tooltip);
    }

    /// <summary>Launches a fresh editor process and exits this one. Settings are saved by the process-exit handler.</summary>
    private static void RestartEditor()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            Log.Warning("Could not restart: the executable path is unknown.");
            return;
        }

        try
        {
            Process.Start(exePath);
        }
        catch (Exception e)
        {
            Log.Warning($"Could not restart the editor: {e.Message}");
            return;
        }

        Environment.Exit(0);
    }

    private void DrawTestFeaturesTab()
    {
        FormInputs.AddSectionHeader($"Test latest features in v{FileLocations.TixlVersion}");
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
                    if (DrawTestRow(set, _selectedTestSetId == set.Id))
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

    private bool DrawTestRow(TestSet set, bool selected)
    {
        var scale = T3Ui.UiScaleFactor;
        var dl = ImGui.GetWindowDrawList();
        ImGui.PushID(set.Id);

        var rowSize = new Vector2(ImGui.GetContentRegionAvail().X, 44 * scale);
        var clicked = ImGui.InvisibleButton("##row", rowSize);
        var hovered = ImGui.IsItemHovered();
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();

        var bg = selected
                     ? UiColors.StatusActivated.Fade(hovered ? 1f : 0.9f)
                     : hovered
                         ? UiColors.ForegroundFull.Fade(0.1f)
                         : UiColors.ForegroundFull.Fade(0.04f);
        dl.AddRectFilled(min, max, bg, 4 * scale);

        var textColor = selected ? UiColors.ForegroundFull : UiColors.Text;
        var muted = selected ? UiColors.ForegroundFull.Fade(0.75f) : UiColors.TextMuted;
        var padX = 12 * scale;
        var padY = 5 * scale;

        dl.AddText(Fonts.FontBold, Fonts.FontBold.FontSize, new Vector2(min.X + padX, min.Y + padY), textColor, set.Title);
        if (!string.IsNullOrEmpty(set.Scope))
            dl.AddText(Fonts.FontSmall, Fonts.FontSmall.FontSize,
                       new Vector2(min.X + padX, min.Y + padY + Fonts.FontBold.FontSize + 2 * scale), muted, set.Scope);

        var steps = $"{set.Steps.Count} step{(set.Steps.Count == 1 ? "" : "s")}";
        ImGui.PushFont(Fonts.FontSmall);
        var stepsWidth = ImGui.CalcTextSize(steps).X;
        ImGui.PopFont();
        dl.AddText(Fonts.FontSmall, Fonts.FontSmall.FontSize, new Vector2(max.X - padX - stepsWidth, min.Y + padY), muted, steps);

        var rightX = max.X - padX;
        DrawTagPills(dl, set.Tags, ref rightX, max.Y - padY - Fonts.FontSmall.FontSize, muted, scale);

        ImGui.PopID();
        return clicked;
    }

    private static void DrawTagPills(ImDrawListPtr dl, IReadOnlyList<string> tags, ref float rightX, float y, Color textColor, float scale)
    {
        if (tags.Count == 0)
            return;

        ImGui.PushFont(Fonts.FontSmall);
        var pillPadX = 6 * scale;
        var pillPadY = 1 * scale;
        var fontSize = Fonts.FontSmall.FontSize;
        var bgColor = ImGui.GetColorU32(UiColors.ForegroundFull.Fade(0.12f).Rgba);
        var fgColor = ImGui.GetColorU32(textColor.Rgba);

        for (var i = tags.Count - 1; i >= 0; i--)
        {
            var label = tags[i].ToUpperInvariant();
            var w = ImGui.CalcTextSize(label).X + pillPadX * 2;
            rightX -= w;
            dl.AddRectFilled(new Vector2(rightX, y - pillPadY), new Vector2(rightX + w, y + fontSize + pillPadY), bgColor, 6 * scale);
            dl.AddText(Fonts.FontSmall, fontSize, new Vector2(rightX + pillPadX, y), fgColor, label);
            rightX -= 4 * scale;
        }

        ImGui.PopFont();
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

    private const string StableWelcomeMarkdown =
        "Each TiXL version keeps its own folders for Settings and Projects, so different versions don't overwrite each other. Use the import options on the left to copy data from a previous version.";

    private readonly MarkdownView _welcomeMarkdown = new(new MarkdownView.Options());
    private readonly MarkdownView _releaseNotesMarkdown = new(new MarkdownView.Options());
    private string? _releaseNotes;

    private bool _isInitialized;
    private bool _isAlpha;
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
