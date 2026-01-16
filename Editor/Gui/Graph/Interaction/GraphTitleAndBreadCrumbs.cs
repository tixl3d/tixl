using System.Diagnostics;
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Editor.Gui.Styling;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.Interaction;

internal static class GraphTitleAndBreadCrumbs
{
    public static void Draw(ProjectView window)
    {
        if (window.InstView == null)
            return;
            
        DrawBreadcrumbs(window);
        DrawNameAndDescription(window.InstView);
    }

    private static void DrawBreadcrumbs(ProjectView projectView)
    {
        var composition = projectView.InstView;
        Debug.Assert(composition != null);
        ImGui.SetCursorScreenPos(ImGui.GetWindowPos() + new Vector2(1, 1));
        var frameHeight = ImGui.GetFrameHeight();
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UiColors.BackgroundActive.Rgba);
        
        if(CustomComponents.IconButton(Icon.Hub, Vector2.One * frameHeight))
        {
            projectView.Close();
        }
        ImGui.PopStyleColor();
        CustomComponents.TooltipForLastItem("Project Hub");
        
        //FormInputs.AddVerticalSpace();
        if (composition.Instance.Parent == null)
            return;
        
        //ImGui.SameLine();
        var parents = Structure.CollectParentInstances(composition.Instance);
        
        ImGui.PushStyleColor(ImGuiCol.Button, Color.Transparent.Rgba);
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(1, 1));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, 0));
        {
            var isFirstChild = true;
            foreach (var p in parents)
            {
                if (isFirstChild)
                {
                    DrawSeparator(_breadCrumbProject, 8);
                    isFirstChild=false;
                    ImGui.SameLine(0,0);
                }
                else
                {
                    ImGui.SameLine(0);
                }
                
                ImGui.PushID(p.SymbolChildId.GetHashCode());

                ImGui.PushFont(Fonts.FontSmall);
                var clicked = ImGui.Button(p.Symbol.Name, new Vector2(0, frameHeight));
                ImGui.PopFont();
                        
                if (p.Parent == null && ImGui.BeginItemTooltip())
                {
                    PopulateDependenciesTooltip(p);
                    ImGui.EndTooltip();
                }

                if (clicked)
                {
                    projectView.TrySetCompositionOpToParent();
                    break;
                }

                DrawSeparator(_breadCrumbSeparator);
            }
        }
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(2);
        return;

        void DrawSeparator(string iconString, float padding = 0)
        {
            ImGui.SameLine(0, padding);
            ImGui.PopID();
            ImGui.PushFont(Icons.IconFont);
            var yPadding = (frameHeight - Icons.IconFont.FontSize) / 2;
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(10,yPadding));
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(iconString);
            ImGui.PopStyleVar();
            ImGui.PopFont();
        }
    }

    private static void PopulateDependenciesTooltip(Instance p)
    {
        var project = p.Symbol.SymbolPackage;
        ImGui.Text("Project: " + project.DisplayName);
        ImGui.NewLine();
        ImGui.Text("Dependencies:");

        foreach (var dependency in project.Dependencies)
        {
            ImGui.Text(dependency.ToString());
        }
    }

    private static void DrawNameAndDescription(ProjectView.InstanceView compositionOp)
    {
        ImGui.SetCursorPosX(8);
        ImGui.PushFont(Fonts.FontLarge);
        ImGui.TextUnformatted(compositionOp.Symbol.Name);

        if (compositionOp.Instance.Parent == null && ImGui.BeginItemTooltip())
        {
            ImGui.PushFont(Fonts.FontNormal);
            PopulateDependenciesTooltip(compositionOp.Instance);
            ImGui.PopFont();
            ImGui.EndTooltip();
        }
                
        ImGui.SameLine();

        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.ForegroundFull.Fade(0.3f).Rgba);
        ImGui.TextUnformatted("  - " + compositionOp.Symbol.Namespace);
        ImGui.PopFont();
        ImGui.PopStyleColor();
    }
    private static readonly string _breadCrumbSeparator = (char)Icon.ChevronRight + "";
    private static readonly string _breadCrumbProject = (char)Icon.FolderOpen + "";
}