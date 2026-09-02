#nullable enable
using System.Threading.Tasks;
using ImGuiNET;
using T3.Core.Compilation;
using T3.Core.Stats;
using T3.Core.SystemUi;
using T3.Core.Utils;
using T3.Editor.Gui.Graph.Dialogs;
using T3.Editor.Gui.Window;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.Keyboard;
using T3.Editor.Gui.Interaction.StartupCheck;
using T3.Editor.Migrations.AssetPaths;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.UiHelpers.Thumbnails;
using T3.Editor.Gui.UiHelpers.Wiki;
using T3.Editor.Gui.Windows;
using T3.Editor.Gui.Windows.Analyze;
using T3.Editor.Gui.Windows.Layouts;
using T3.Editor.Gui.Windows.RenderExport;
using T3.Editor.Skills.Ui;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Exporting;
using T3.Editor.UiModel.Helpers;
using T3.Editor.UiModel.Modification;
using T3.Editor.UiModel.ProjectHandling;
using ShaderCompiler = T3.Core.Resource.ShaderCompiling.ShaderCompiler;

namespace T3.Editor.Gui;

internal static class AppMenuBar
{
    internal static void DrawAppMenuBar()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6, 6) * T3Ui.UiScaleFactor);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, T3Style.WindowPaddingForMenus * T3Ui.UiScaleFactor);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0);

        if (ImGui.BeginMainMenuBar())
        {
            
            
            // Enable app menu after click if only visible during hover
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                UserSettings.Config.ShowMainMenu = true;
            }

            ImGui.SetCursorPos(new Vector2(0, -1)); // Shift to make menu items selected when hitting top of screen

            ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1);
            ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, 3);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 8));

            ImGui.PushStyleColor(ImGuiCol.Separator, UiColors.BackgroundFull.Fade(0.5f).Rgba);

            DrawMainMenu();

            ImGui.PopStyleColor();
            ImGui.PopStyleVar(3);

            DrawVersionIndicator();
            DrawErrorsIndicator();
            T3Metrics.DrawRenderPerformanceGraph();

            ImGui.SameLine();

            if (!UserSettings.Config.EnableKeyboardShortCuts)
            {
                ImGui.TextColored(UiColors.StatusAttention, "Keyboard Disabled!");
                if (ImGui.IsItemClicked())
                {
                    UserSettings.Config.EnableKeyboardShortCuts = true;
                }
                ImGui.SameLine();
            }

            Program.StatusErrorLine.Draw();
            DrawDebugServerIndicator();

            ImGui.EndMainMenuBar();
        }

        ImGui.PopStyleVar(3);
    }

    private static void DrawDebugServerIndicator()
    {
        if (!App.DebugProtocol.DebugServer.IsRunning)
            return;

        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetWindowWidth() - ImGui.GetFrameHeight());

        // Flash on protocol traffic, decaying to a dim resting state.
        var msSinceActivity = Environment.TickCount64 - App.DebugProtocol.DebugServer.LastActivityTicksMs;
        var flash = Math.Clamp(1f - msSinceActivity / 400f, 0f, 1f);
        var color = UiColors.StatusAttention.Fade(0.4f + 0.6f * flash);
        CustomComponents.IconButton(Icon.IO, Vector2.Zero, color);
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted($"Debug server listening on 127.0.0.1:{App.DebugProtocol.DebugServer.Port}"
                                  + $" — {App.DebugProtocol.DebugServer.RequestCount} requests handled");

            var messages = App.DebugProtocol.DebugServer.RecentMessages;
            if (messages.Count > 0)
            {
                ImGui.Spacing();
                ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
                foreach (var message in messages)
                {
                    ImGui.TextUnformatted(message);
                }

                ImGui.PopStyleColor();
            }

            ImGui.EndTooltip();
        }
    }

    private static void DrawErrorsIndicator()
    {
        ImGui.SameLine(0, AppBarSpacingX);
        var isHovered = false;
        {
            var hasErrors = OperatorDiagnostics.StatusUpdates.Count > 0;

            if (!hasErrors)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
                Icon.Checkmark.DrawAtCursor();
                ImGui.PopStyleColor();
            }
            else
            {
                var timeSinceChange = (float)(ImGui.GetTime() - OperatorDiagnostics.LastChangeTime).Clamp(0, 1);
                ImGui.PushStyleVar(ImGuiStyleVar.Alpha, MathUtils.Lerp(1, 0.4f, timeSinceChange));
                Icon.Warning.DrawAtCursor();
                isHovered = ImGui.IsItemHovered();

                ImGui.SameLine(0, 0);
                ImGui.TextUnformatted($"{OperatorDiagnostics.StatusUpdates.Count}");
                ImGui.PopStyleVar();
            }

            var isGroupHovered = isHovered || ImGui.IsItemHovered();
            if (!isGroupHovered)
                return;

            CustomComponents.BeginTooltip(1200);
            {
                if (OperatorDiagnostics.StatusUpdates.Count == 0)
                {
                    ImGui.TextUnformatted("No problems");
                }
                else
                {
                    if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                    {
                        OperatorDiagnostics.StatusUpdates.Clear();
                    }

                    CustomComponents.HelpText("Recent Operators problems. Click to clear...");

                    foreach (var x in OperatorDiagnostics.StatusUpdates.Values.OrderByDescending(x => x.Time))
                    {
                        if (Structure.TryGetInstanceFromPath(x.IdPath, out _, out var readableInstancePath))
                        {
                            var timeSince = ImGui.GetTime() - x.Time;
                            var fadeLine = ((float)timeSince).RemapAndClamp(10, 100, 1, 0.4f);

                            ImGui.PushFont(Fonts.FontSmall);
                            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, fadeLine);
                            var notFirst = false;
                            foreach (var p in readableInstancePath)
                            {
                                if (notFirst)
                                {
                                    ImGui.SameLine(0, 4);
                                    ImGui.TextColored(UiColors.TextMuted, "/");
                                    ImGui.SameLine(0, 4);
                                }

                                notFirst = true;

                                ImGui.TextUnformatted(p);
                            }

                            ImGui.SameLine(0, 10);
                            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
                            ImGui.TextUnformatted(StringUtils.GetReadableRelativeTime(timeSince));
                            ImGui.PopStyleColor();
                            ImGui.PopFont();

                            var color = x.Statuses switch
                                            {
                                                OperatorDiagnostics.Statuses.HasWarning => UiColors.StatusAttention,
                                                OperatorDiagnostics.Statuses.HasError   => UiColors.StatusError,
                                                _                                       => UiColors.TextMuted
                                            };

                            ImGui.PushStyleColor(ImGuiCol.Text, color.Rgba);
                            ImGui.TextUnformatted(x.Message);
                            ImGui.PopStyleColor();
                            ImGui.PopStyleVar();
                            FormInputs.AddVerticalSpace(1);
                        }
                    }
                }
            }
            CustomComponents.EndTooltip();
        }
    }

    private static void DrawVersionIndicator()
    {
        if (!UserSettings.Config.FullScreen)
            return;

        ImGui.SameLine(0, AppBarSpacingX);
        //ImGui.Dummy(new Vector2(10, 10));
        //ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 2);

        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.ForegroundFull.Fade(0.2f).Rgba);
        ImGui.TextUnformatted(Program.FormattedEditorVersion);
        ImGui.PopStyleColor();
    }

    
    // Thin wrappers over the styled menu components so the AppMenu dropdowns match the graph context menu.
    // The label seeds a stable id (DrawMenuItem also keys its hit-test off the label), and the active
    // toggle is emphasized while everything else is muted — same model as the context menu.
    private static bool MenuItem(string label, string? shortcut = null, bool isChecked = false, bool isEnabled = true)
        => CustomComponents.DrawMenuItem(label.GetHashCode(), Icon.None, label, shortcut, isChecked, isEnabled,
                                         reserveIconColumn: false,
                                         state: isChecked ? CustomComponents.ButtonStates.Emphasized : CustomComponents.ButtonStates.Default);

    private static bool MenuItemToggle(string label, ref bool value, string? shortcut = null)
    {
        var clicked = MenuItem(label, shortcut, isChecked: value);
        if (clicked)
            value = !value;

        return clicked;
    }

    // Nested submenu header (NOT for the horizontal top-level bar entries, which stay native ImGui.BeginMenu).
    private static bool BeginSubMenu(string label, bool isEnabled = true)
        => CustomComponents.DrawSubMenu(label.GetHashCode(), label, isEnabled);

    private static void DrawMainMenu()
    {
        if (ImGui.BeginMenu("TiXL"))
        {
            var currentProject = ProjectView.Focused?.OpenedProject.Package;
            UserSettings.Config.ShowMainMenu = true;

            var showNewTemplateOption = !T3Ui.IsCurrentlySaving && currentProject != null;
            
            if (MenuItem("New Project..."))
            {
                T3Ui.NewProjectDialog.ShowNextFrame();
            }

            if (MenuItem("New Operator...", UserActions.New.ListShortcuts(), isEnabled: showNewTemplateOption))
            {
                T3Ui.CreateFromTemplateDialog.ShowNextFrame();
            }

            if (BeginSubMenu("Recent Projects", !T3Ui.IsCurrentlySaving && EditableSymbolProject.AllProjects.Any(x => x.HasHome)))
            {
                foreach (var package in EditableSymbolProject.AllProjects)
                {
                    if (!package.HasHome)
                        continue;

                    var name = package.DisplayName;

                    if (MenuItem(name))
                    {
                        if (GraphWindow.GraphWindowInstances.Count > 0)
                        {
                            BlockingWindow.Instance.ShowMessageBox("Would you like to open this project in a new window?", "Opening " + name, "Yes", "No");
                        }

                        Log.Error("Not implemented yet");
                    }
                }
                
                ImGui.EndMenu();
            }

            if (currentProject is { IsReadOnly: false } && currentProject is EditableSymbolProject project)
            {
                //ImGui.Separator();

                if (BeginSubMenu("Open Project in"))
                {
                    if (MenuItem("File Explorer"))
                    {
                        CoreUi.Instance.OpenWithDefaultApplication(project.Folder);
                    }

                    if (MenuItem("Resource Folder"))
                    {
                        CoreUi.Instance.OpenWithDefaultApplication(project.AssetsFolder);
                    }

                    if (MenuItem("Development IDE"))
                    {
                        CoreUi.Instance.OpenWithDefaultApplication(project.CsProjectFile.FullPath);
                    }

                    ImGui.EndMenu();
                }
            }

            CustomComponents.SeparatorLine();

            // Disabled, at least for now, as this is an incomplete (or not even started) operation on the Main branch atm
            if (MenuItem("Import Operators", isEnabled: !T3Ui.IsCurrentlySaving))
            {
                BlockingWindow.Instance.ShowMessageBox("This feature is not yet available in the main branch. Stay tuned for updates!",
                                                       "Not yet implemented");
                //_importDialog.ShowNextFrame();
            }

            CustomComponents.SeparatorLine();

            if (MenuItem("Save Changes", UserActions.Save.ListShortcuts(), isEnabled: !T3Ui.IsCurrentlySaving))
            {
                T3Ui.SaveInBackground(saveAll: false);
            }

            if (MenuItem("Save All", isEnabled: !T3Ui.IsCurrentlySaving))
            {
                T3Ui.SaveInBackground(saveAll: true);
            }

            if (MenuItem("Set Project Thumbnail", isEnabled: RenderProcess.MainOutputTexture != null))
            {
                if (currentProject != null && RenderProcess.MainOutputTexture != null)
                {
                    ThumbnailManager.SaveThumbnail(currentProject.Id, currentProject, RenderProcess.MainOutputTexture, ThumbnailManager.Categories.PackageMeta);
                }
            }

            CustomComponents.SeparatorLine();

            if (BeginSubMenu("Development Tools"))
            {
                if (MenuItem("Skill Map Editor"))
                    SkillMapEditor.ShowNextFrame();

                if (MenuItem("Tour Point Editor"))
                    EditTourPointsPopup.ShowNextFrame();

                CustomComponents.SeparatorLine();

                if (MenuItem("Fix Asset Paths"))
                    ConformAssetPaths.ConformAllPaths();

                if (MenuItem("Check Symbol Dependencies"))
                {
                    SymbolAnalysis.LogInvalidSymbolDependencies();
                    SymbolAnalysis.LogInvalidAssetReference();
                }

                if (BeginSubMenu("Clear Shader Cache"))
                {
                    if (MenuItem("Editor Only"))
                        ShaderCompiler.DeleteShaderCache(all: false);

                    if (MenuItem("All Editor and Player Versions"))
                        ShaderCompiler.DeleteShaderCache(all: true);

                    ImGui.EndMenu();
                }

                if (BeginSubMenu("Documentation"))
                {
                    if (MenuItem("Export as WIKI"))
                        ExportWikiDocumentation.ExportWiki();

                    if (MenuItem("Export to JSON"))
                        ExportDocumentationStrings.ExportDocumentationAsJson();

                    if (MenuItem("Import from JSON"))
                        ExportDocumentationStrings.ImportDocumentationAsJson();

                    ImGui.EndMenu();
                }

                if (BeginSubMenu("Debug"))
                {
                    if (MenuItem("ImGUI Demo", isChecked: WindowManager.DemoWindowVisible))
                        WindowManager.DemoWindowVisible = !WindowManager.DemoWindowVisible;

                    if (MenuItem("ImGUI Metrics", isChecked: WindowManager.MetricsWindowVisible))
                        WindowManager.MetricsWindowVisible = !WindowManager.MetricsWindowVisible;

                    ImGui.EndMenu();
                }

                WindowManager.UtilitiesWindow.DrawMenuItemToggle();
                WindowManager.GuidedFeatureTestsWindow.DrawMenuItemToggle();

                ImGui.EndMenu();
            }

            CustomComponents.SeparatorLine();

            WindowManager.SettingsWindow.DrawMenuItemToggle();
            {
                var hasSettings = ProjectView.Focused?.CompositionInstance?.Symbol.CompositionSettings is { Enabled: true };
                var window = WindowManager.ProjectSettingsWindow;
                if (MenuItem("Composition Settings", isChecked: hasSettings))
                {
                    window.Config.Visible = !window.Config.Visible;
                }
            }

            CustomComponents.SeparatorLine();

            if (MenuItem("Exit", isEnabled: !T3Ui.IsCurrentlySaving))
            {
                T3Ui.ExitDialog.ShowNextFrame();
            }

            if (ImGui.IsItemHovered() && T3Ui.IsCurrentlySaving)
            {
                ImGui.SetTooltip("Can't exit while saving is in progress");
            }

            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Edit"))
        {
            UserSettings.Config.ShowMainMenu = true;
            if (MenuItem("Undo", "CTRL+Z", isEnabled: UndoRedoStack.CanUndo))
            {
                UndoRedoStack.Undo();
            }

            if (MenuItem("Redo", "CTRL+SHIFT+Z", isEnabled: UndoRedoStack.CanRedo))
            {
                UndoRedoStack.Redo();
            }

            CustomComponents.SeparatorLine();

            var sectionView = ProjectView.Focused;
            var canAddSection = sectionView?.CompositionInstance != null && !T3Ui.IsCurrentlySaving;
            if (MenuItem("Add Section", UserActions.AddSection.ListShortcuts(), isEnabled: canAddSection))
            {
                // Placed around the selection, or at the center of the graph view
                var canvas = sectionView!.GraphView.Canvas;
                var centerScreen = canvas.WindowPos + canvas.WindowSize / 2;
                NodeActions.AddSection(sectionView.NodeSelection, canvas, sectionView.CompositionInstance!, centerScreen);
                sectionView.GraphView.FlagStructureAsChanged();
            }

            CustomComponents.SeparatorLine();

            if (BeginSubMenu("Bookmarks"))
            {
                GraphBookmarkNavigation.DrawBookmarksMenu();
                ImGui.EndMenu();
            }

            CustomComponents.SeparatorLine();

            var exportView = ProjectView.Focused;
            var exportChildUis = exportView?.NodeSelection.GetSelectedChildUis().ToList();
            var canExport = exportView?.CompositionInstance != null && exportChildUis is { Count: 1 };
            if (MenuItem("Export as Executable", isEnabled: canExport))
            {
                PlayerExporter.ExportAndReport(exportView!.CompositionInstance!, exportChildUis![0]);
            }

            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("View"))
        {
            UserSettings.Config.ShowMainMenu = true;

            CustomComponents.DrawMenuGroupLabel("UI Elements");
            MenuItemToggle("Main Menu", ref UserSettings.Config.ShowMainMenu);
            MenuItemToggle("Graph Title", ref UserSettings.Config.ShowTitleAndDescription);
            MenuItemToggle("Graph Minimap", ref UserSettings.Config.ShowMiniMap);
            MenuItemToggle("Graph Toolbar", ref UserSettings.Config.ShowToolbar);
            MenuItemToggle("Timeline", ref UserSettings.Config.ShowTimeline);
            if (MenuItem("Toggle All", UserActions.ToggleAllUiElements.ListShortcuts(), isEnabled: !T3Ui.IsCurrentlySaving))
            {
                UiConfig.ToggleAllUiElements();
            }

            CustomComponents.SeparatorLine();

            MenuItemToggle("Interactions Overlay", ref UserSettings.Config.ShowInteractionOverlay);
            CustomComponents.SeparatorLine();
            MenuItemToggle("Fullscreen UI", ref UserSettings.Config.FullScreen, UserActions.ToggleFullscreen.ListShortcuts());
            CustomComponents.SeparatorLine();

            if (MenuItem("Focus Mode", UserActions.ToggleFocusMode.ListShortcuts(), isChecked: LayoutHandling.FocusMode))
            {
                UiConfig.ToggleFocusMode();
            }

            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Windows"))
        {
            WindowManager.DrawWindowMenuContent();
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Help"))
        {
            if (MenuItem(RuntimeAssemblies.IsAlpha ? "Welcome to Alpha" : "Welcome"))
            {
                WindowManager.WelcomeWindow.Open();
            }

            WindowManager.HelpWindow.DrawMenuItemToggle();

            CustomComponents.SeparatorLine();

            if (BeginSubMenu("Documentation"))
            {
                foreach (var link in _helpLinks)
                {
                    link.DrawMenuItem();
                }

                ImGui.EndMenu();
            }

            if (BeginSubMenu("YouTube"))
            {
                foreach (var link in _youTubeLinks)
                {
                    link.DrawMenuItem();
                }

                ImGui.EndMenu();
            }

            CustomComponents.SeparatorLine();

            foreach (var link in _otherLinks)
            {
                link.DrawMenuItem();
            }

            CustomComponents.SeparatorLine();

            if (BeginSubMenu("Send Feedback"))
            {
                foreach (var link in _feedbackLinks)
                {
                    link.DrawMenuItem();
                }

                ImGui.EndMenu();
            }

            CustomComponents.SeparatorLine();

            _licenseLink.DrawMenuItem();

            if (MenuItem("About TiXL"))
            {
                T3Ui.AboutDialog.ShowNextFrame();
            }

            ImGui.EndMenu();
        }
    }

    private sealed class HelpLink(string title, string url, string toolTip = "")
    {
        private string Title { get; } = title;
        private string Url { get; } = url;
        private string ToolTip { get; } = toolTip;

        public void DrawMenuItem()
        {
            if (MenuItem(Title))
            {
                CoreUi.Instance.OpenWithDefaultApplication(Url);
            }

            CustomComponents.TooltipForLastItem("Open link in browser", ToolTip);
        }
    }

    private const string GitHubBaseUrl = "https://github.com/tixl3d/tixl/";
    private const string WikiRootUrl = GitHubBaseUrl + "wiki/";

    private static readonly List<HelpLink> _helpLinks =
        [
            new("Introduction", WikiRootUrl + "help.Introduction"),
            new("Operator Library", WikiRootUrl + "lib"),
            new("FAQ", WikiRootUrl + "help.FAQ"),
            new("Video Tutorials", WikiRootUrl + "help.VideoTutorials"),
            new("Using Backups", WikiRootUrl + "help.Backups"),
        ];

    private static readonly List<HelpLink> _youTubeLinks =
        [
            new("Getting Started (15min)", "https://www.youtube.com/watch?v=_zvzX0fZ8sc"),
            new("Tutorials (Playlist)", "https://www.youtube.com/playlist?list=PLj-rnPROvbn3LigXGRSDvmLtgTwmNHcQs"),
            new("Tip of the Day (Playlist)", "https://www.youtube.com/watch?v=Jpvyg-LR3f0&list=PLj-rnPROvbn2cfnUwuyb5gRj-juOYUC7T"),
            new("Made with TiXL", "https://www.youtube.com/playlist?list=PLj-rnPROvbn3LNU34daaRk5EiaXlwo2E0"),
        ];

    private static readonly List<HelpLink> _otherLinks =
        [
            new("TiXL Web-Site", "https://tixl.app"),
            new("Latest Releases", "https://github.com/tixl3d/tixl/releases"),

            new("Discord Community", "https://discord.com/invite/YmSyQdeH3S",
                "Join a friendly and welcoming community of enthusiasts. Ask questions, Learn from each other, share or just hang out."),
            new("Meet Up (Every 2nd Week)", "https://discord.com/invite/WX94pzKj?event=1359348185914544312",
                "We meet every 2nd week to share our screens answer questions and hang out."),
        ];

    private static readonly List<HelpLink> _feedbackLinks =
        [
            new("Report Issue", GitHubBaseUrl + "/issues/new?template=bug_report.md", "Please search for other related issues before posting..."),
            new("Request Feature", GitHubBaseUrl + "/issues/new?template=feature-request.md", "Please search for other related issues before posting..."),
        ];

    private static readonly HelpLink _licenseLink = new("MIT License", GitHubBaseUrl + "?tab=MIT-1-ov-file#readme");

    public static readonly float AppBarSpacingX = 20;
}