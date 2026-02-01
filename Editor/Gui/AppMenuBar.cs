#nullable enable
using System.Threading.Tasks;
using ImGuiNET;
using T3.Core.Resource;
using T3.Core.Stats;
using T3.Core.SystemUi;
using T3.Core.Utils;
using T3.Editor.Gui.Graph.Dialogs;
using T3.Editor.Gui.Window;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.Keyboard;
using T3.Editor.Gui.Interaction.StartupCheck;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.UiHelpers.Wiki;
using T3.Editor.Gui.Windows;
using T3.Editor.Gui.Windows.Layouts;
using T3.Editor.Skills.Data;
using T3.Editor.Skills.Ui;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.ProjectHandling;

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

            ImGui.EndMainMenuBar();
        }

        ImGui.PopStyleVar(3);
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

    private static void DrawMainMenu()
    {
        if (ImGui.BeginMenu("TiXL"))
        {
            UserSettings.Config.ShowMainMenu = true;

            var currentProject = ProjectView.Focused?.OpenedProject.Package;
            var showNewTemplateOption = !T3Ui.IsCurrentlySaving && currentProject != null;
            
            if (ImGui.MenuItem("New Project..."))
            {
                T3Ui.NewProjectDialog.ShowNextFrame();
            }

            if (ImGui.MenuItem("New Operator...", UserActions.New.ListShortcuts(), false, showNewTemplateOption))
            {
                T3Ui.CreateFromTemplateDialog.ShowNextFrame();
            }

            if (ImGui.BeginMenu("Recent Projects...", !T3Ui.IsCurrentlySaving && EditableSymbolProject.AllProjects.Any(x => x.HasHome)))
            {
                foreach (var package in EditableSymbolProject.AllProjects)
                {
                    if (!package.HasHome)
                        continue;

                    var name = package.DisplayName;

                    if (ImGui.MenuItem(name))
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

                if (ImGui.BeginMenu("Open Project in..."))
                {
                    if (ImGui.MenuItem("File Explorer"))
                    {
                        CoreUi.Instance.OpenWithDefaultApplication(project.Folder);
                    }

                    if (ImGui.MenuItem("Resource Folder"))
                    {
                        CoreUi.Instance.OpenWithDefaultApplication(project.AssetsFolder);
                    }

                    if (ImGui.MenuItem("Development IDE"))
                    {
                        CoreUi.Instance.OpenWithDefaultApplication(project.CsProjectFile.FullPath);
                    }

                    ImGui.EndMenu();
                }
            }

            ImGui.Separator();

            // Disabled, at least for now, as this is an incomplete (or not even started) operation on the Main branch atm
            if (ImGui.MenuItem("Import Operators", null, false, !T3Ui.IsCurrentlySaving))
            {
                BlockingWindow.Instance.ShowMessageBox("This feature is not yet available in the main branch. Stay tuned for updates!",
                                                       "Not yet implemented");
                //_importDialog.ShowNextFrame();
            }

            ImGui.Separator();
            if (ImGui.MenuItem("Disable Keyboard ShortCuts", UserActions.Save.ListShortcuts(), !UserSettings.Config.EnableKeyboardShortCuts))
            {
                UserSettings.Config.EnableKeyboardShortCuts = !UserSettings.Config.EnableKeyboardShortCuts;
            }
            
            ImGui.Separator();

            if (ImGui.MenuItem("Save Changes", UserActions.Save.ListShortcuts(), false, !T3Ui.IsCurrentlySaving))
            {
                T3Ui.SaveInBackground(saveAll: false);
            }

            if (ImGui.MenuItem("Save All", !T3Ui.IsCurrentlySaving))
            {
                Task.Run(() => { T3Ui.Save(true); });
            }

            ImGui.Separator();

            if (ImGui.BeginMenu("Development Tools"))
            {
                if (ImGui.MenuItem("Skill Map Editor"))
                    SkillMapEditor.ShowNextFrame();
                
                if (ImGui.MenuItem("Tour Point Editor"))
                    EditTourPointsPopup.ShowNextFrame();
                
                ImGui.Separator();

                if (ImGui.MenuItem("Fix asset paths"))
                    ConformAssetPaths.ConformAllPaths();
                
                if (ImGui.BeginMenu("Clear shader cache"))
                {
                    if (ImGui.MenuItem("Editor only"))
                        ShaderCompiler.DeleteShaderCache(all: false);

                    if (ImGui.MenuItem("All editor and player versions"))
                        ShaderCompiler.DeleteShaderCache(all: true);

                    ImGui.EndMenu();
                }

                if (ImGui.BeginMenu("Documentation"))
                {
                    if (ImGui.MenuItem("Export as WIKI"))
                        ExportWikiDocumentation.ExportWiki();

                    if (ImGui.MenuItem("Export to JSON"))
                        ExportDocumentationStrings.ExportDocumentationAsJson();

                    if (ImGui.MenuItem("Import from JSON"))
                        ExportDocumentationStrings.ImportDocumentationAsJson();

                    ImGui.EndMenu();
                }

                if (ImGui.BeginMenu("Debug"))
                {

                    
                    if (ImGui.MenuItem("ImGUI Demo", "", WindowManager.DemoWindowVisible))
                        WindowManager.DemoWindowVisible = !WindowManager.DemoWindowVisible;

                    if (ImGui.MenuItem("ImGUI Metrics", "", WindowManager.MetricsWindowVisible))
                        WindowManager.MetricsWindowVisible = !WindowManager.MetricsWindowVisible;

                    ImGui.EndMenu();
                }

                WindowManager.UtilitiesWindow.DrawMenuItemToggle();

                ImGui.EndMenu();
            }

            ImGui.Separator();

            WindowManager.SettingsWindow.DrawMenuItemToggle();

            ImGui.Separator();

            if (ImGui.MenuItem("Exit", !T3Ui.IsCurrentlySaving))
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
            if (ImGui.MenuItem("Undo", "CTRL+Z", false, UndoRedoStack.CanUndo))
            {
                UndoRedoStack.Undo();
            }

            if (ImGui.MenuItem("Redo", "CTRL+SHIFT+Z", false, UndoRedoStack.CanRedo))
            {
                UndoRedoStack.Redo();
            }

            ImGui.Separator();

            if (ImGui.BeginMenu("Bookmarks"))
            {
                GraphBookmarkNavigation.DrawBookmarksMenu();
                ImGui.EndMenu();
            }

            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("View"))
        {
            UserSettings.Config.ShowMainMenu = true;

            CustomComponents.MenuGroupHeader("UI Elements...");
            ImGui.MenuItem("Main Menu", "", ref UserSettings.Config.ShowMainMenu);
            ImGui.MenuItem("Graph Title", "", ref UserSettings.Config.ShowTitleAndDescription);
            ImGui.MenuItem("Graph Minimap", "", ref UserSettings.Config.ShowMiniMap);
            ImGui.MenuItem("Graph Toolbar", "", ref UserSettings.Config.ShowToolbar);
            ImGui.MenuItem("Timeline", "", ref UserSettings.Config.ShowTimeline);
            if (ImGui.MenuItem("Toggle All", UserActions.ToggleAllUiElements.ListShortcuts(), false,
                               !T3Ui.IsCurrentlySaving))
            {
                UiConfig.ToggleAllUiElements();
            }

            ImGui.Separator();

            ImGui.MenuItem("Interactions Overlay", "", ref UserSettings.Config.ShowInteractionOverlay);
            ImGui.Separator();
            ImGui.MenuItem("Fullscreen UI", UserActions.ToggleFullscreen.ListShortcuts(), ref UserSettings.Config.FullScreen);
            ImGui.Separator();

            if (ImGui.MenuItem("Focus Mode", UserActions.ToggleFocusMode.ListShortcuts(), UserSettings.Config.FocusMode))
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
            if (ImGui.BeginMenu("Documentation"))
            {
                foreach (var link in _helpLinks)
                {
                    link.DrawMenuItem();
                }

                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("YouTube"))
            {
                foreach (var link in _youTubeLinks)
                {
                    link.DrawMenuItem();
                }

                ImGui.EndMenu();
            }

            ImGui.Separator();

            foreach (var link in _otherLinks)
            {
                link.DrawMenuItem();
            }

            ImGui.Separator();

            if (ImGui.BeginMenu("Send Feedback"))
            {
                foreach (var link in _feedbackLinks)
                {
                    link.DrawMenuItem();
                }

                ImGui.EndMenu();
            }
            ImGui.Separator();

            _licenseLink.DrawMenuItem();

            if (ImGui.MenuItem("About TiXL"))
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
            if (ImGui.MenuItem(Title, null, false))
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
            new("Meet Up (every 2nd week)", "https://discord.com/invite/WX94pzKj?event=1359348185914544312",
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