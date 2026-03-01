#nullable enable

using ImGuiNET;
using T3.Core.Resource;
using T3.Core.SystemUi;
using T3.Editor.Compilation;
using T3.Editor.Gui.Window;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.UiHelpers.Thumbnails;
using T3.Editor.UiModel;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.Hub;

internal static class ProjectsPanel
{
    public static void Draw(GraphWindow window)
    {
        var heightForSkillQuest = UserSettings.Config.ShowSkillQuestInHub ? SkillQuestPanel.Height + 10 : 0;

        ContentPanel.Begin("Projects", null, DrawProjectTools, -heightForSkillQuest);

        FormInputs.AddVerticalSpace(20);

        ImGui.BeginChild("content", new Vector2(0, 0), true, ImGuiWindowFlags.NoBackground);
        {
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(5, 5));

            FormInputs.AddSectionHeader("Projects");

            foreach (var package in EditableSymbolProject.AllProjects)
            {
                DrawProjectItem(window, package);
            }
            
            // 2. Draw Archived Projects (Lightweight)
            if (ProjectSetup.ArchivedProjects.Count > 0)
            {
                ImGui.Separator();
                FormInputs.AddSectionHeader("Archived");
                ImGui.TextDisabled("Note: Loading reactivated projects might require a restart.");
                for (var index = 0; index < ProjectSetup.ArchivedProjects.Count; index++)
                {
                    var archived = ProjectSetup.ArchivedProjects[index];
                    DrawArchivedItem(archived);
                }
            }

            ImGui.PopStyleVar();
        }
        ImGui.EndChild();
        ImGui.PopStyleVar();
        ContentPanel.End();
    }

    private static void DrawProjectTools()
    {
        var addProject = "Add Project...";
        var size = CustomComponents.GetCtaButtonSize(addProject);
        var state = EditableSymbolProject.AllProjects.Any()
                        ? CustomComponents.ButtonStates.Dimmed
                        : CustomComponents.ButtonStates.Activated;
        CustomComponents.RightAlign(size.X + 10 * T3Ui.UiScaleFactor);
        if (CustomComponents.DrawCtaButton(addProject, Icon.None, state))
        {
            T3Ui.NewProjectDialog.ShowNextFrame();
        }
    }

    private static void DrawProjectItem(GraphWindow window, EditorSymbolPackage package)
    {
        if (!package.HasHome)
            return;

        var dl = ImGui.GetWindowDrawList();

        ImGui.PushID(package.DisplayName);
        var isOpened = OpenedProject.OpenedProjects.TryGetValue(package, out var openedProject);
        var name = package.DisplayName;
        var clicked = ImGui.InvisibleButton(name, ProjectItemSize);
        var isHovered = ImGui.IsItemHovered();
        var backgroundColor = isHovered
                                  ? UiColors.ForegroundFull.Fade(0.1f)
                                  : UiColors.ForegroundFull.Fade(0.05f);

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        dl.AddRectFilled(min, max, backgroundColor, 6);

        var padding = 3f * T3Ui.UiScaleFactor;
        if (isOpened)
        {
            dl.AddRectFilled(min + Vector2.One * padding,
                             new Vector2(min.X + padding + 4, max.Y - padding),
                             UiColors.BackgroundActive, 2);
        }

        var rootName = package.RootNamespace.Split(".")[^1];
        if (isOpened)
            rootName += " (loaded)";

        var y = padding;
        var x = 20f;
        dl.AddText(Fonts.FontBold,
                   Fonts.FontBold.FontSize,
                   min + new Vector2(x, y),
                   UiColors.Text, rootName);

        y += Fonts.FontNormal.FontSize + 5;

        dl.AddText(Fonts.FontSmall,
                   Fonts.FontSmall.FontSize,
                   min + new Vector2(x, y),
                   UiColors.TextMuted, package.RootNamespace);

        y += Fonts.FontSmall.FontSize + 5;

        dl.AddText(Fonts.FontSmall,
                   Fonts.FontSmall.FontSize,
                   min + new Vector2(x, y),
                   UiColors.TextMuted, package.Folder);

        var thumbnail = ThumbnailManager.GetThumbnail(package.Id, package, ThumbnailManager.Categories.PackageMeta);
        if (thumbnail.IsReady && ThumbnailManager.AtlasSrv != null)
        {
            var height = ProjectItemSize.Y - padding * 2;
            var size = new Vector2(height * 4 / 3f, height);
            var pos = new Vector2(max.X - size.X - padding, min.Y + padding);
            dl.AddImage(ThumbnailManager.AtlasSrv.NativePointer,
                        pos,
                        pos + size,
                        thumbnail.UvMin, thumbnail.UvMax);
        }

        if (clicked)
        {
            if (!isOpened && package is EditorSymbolPackage editorPackage2)
            {
                if (!OpenedProject.TryCreate(editorPackage2, out openedProject, out var error))
                {
                    Log.Warning($"Failed to load project: {error}");
                    return;
                }
            }

            if (openedProject != null)
            {
                window.TrySetToProject(openedProject);
            }
        }

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(5, 5));
        if (ImGui.BeginPopupContextItem("windows_context_menu"))
        {
            if (ImGui.MenuItem("Reveal in Explorer"))
            {
                CoreUi.Instance.OpenWithDefaultApplication(package.Folder);
            }

            if (ImGui.MenuItem("Unload project", "", false, isOpened))
            {
                if (OpenedProject.TryUnload(package))
                {
                    Log.Debug($"Project '{package.DisplayName}' unloaded successfully");
                }
            }
            
            if(ImGui.MenuItem("Archive project", "", false))
            {
                if (package is EditableSymbolProject editableProject)
                {
                    if (isOpened)
                        OpenedProject.TryUnload(package);
                    
                    ProjectSetup.SetProjectArchived(editableProject.CsProjectFile, true);
                }
            }

            ImGui.EndPopup();
        }

        ImGui.PopStyleVar();

        ImGui.PopID();
    }

    private static void DrawArchivedItem(ProjectSetup.ArchivedProjectInfo archivedProject)
    {
        var dl = ImGui.GetWindowDrawList();

        ImGui.PushID(archivedProject.Name);
        var name = archivedProject.Name;
        var clicked = ImGui.InvisibleButton(name, ProjectItemSize);
        var isHovered = ImGui.IsItemHovered();
        var backgroundColor = isHovered
                                  ? UiColors.ForegroundFull.Fade(0.1f)
                                  : UiColors.ForegroundFull.Fade(0.05f);

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        dl.AddRectFilled(min, max, backgroundColor, 6);

        var padding = 3f * T3Ui.UiScaleFactor;
        
        var rootName = archivedProject.RootNamespace?.Split(".")[^1];

        var y = padding;
        var x = 20f;
        dl.AddText(Fonts.FontBold,
                   Fonts.FontBold.FontSize,
                   min + new Vector2(x, y),
                   UiColors.Text, rootName);

        y += Fonts.FontNormal.FontSize + 5;

        dl.AddText(Fonts.FontSmall,
                   Fonts.FontSmall.FontSize,
                   min + new Vector2(x, y),
                   UiColors.TextMuted, archivedProject.Folder);

        y += Fonts.FontSmall.FontSize + 5;

        dl.AddText(Fonts.FontSmall,
                   Fonts.FontSmall.FontSize,
                   min + new Vector2(x, y),
                   UiColors.TextMuted, archivedProject.Folder);

        var thumbnail = ThumbnailManager.GetThumbnail(archivedProject.Id, archivedProject, ThumbnailManager.Categories.PackageMeta);
        if (thumbnail.IsReady && ThumbnailManager.AtlasSrv != null)
        {
            var height = ProjectItemSize.Y - padding * 2;
            var size = new Vector2(height * 4 / 3f, height);
            var pos = new Vector2(max.X - size.X - padding, min.Y + padding);
            dl.AddImage(ThumbnailManager.AtlasSrv.NativePointer,
                        pos,
                        pos + size,
                        thumbnail.UvMin, thumbnail.UvMax);
        }
        
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(5, 5));
        if (ImGui.BeginPopupContextItem("windows_context_menu"))
        {
            if(ImGui.MenuItem("Reactivate project", "", false))
            {
                ProjectSetup.SetProjectArchived(archivedProject.ProjectFile, false);
            }
            ImGui.EndPopup();
        }

        ImGui.PopStyleVar();

        ImGui.PopID();
    }

    public static Vector2 ProjectItemSize => new Vector2(400, 65) * T3Ui.UiScaleFactor;
}