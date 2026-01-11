using ImGuiNET;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.Windows.Utilities;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.ProjectHandling;

// ReSharper disable PossibleMultipleEnumeration

namespace T3.Editor.Gui.Windows;

internal sealed class SimulatedCrashException : Exception
{
}

internal sealed class UtilitiesWindow : Window
{
    internal UtilitiesWindow()
    {
        Config.Title = "Utilities";
    }

    private bool _crashUnlocked;

    private enum Categories
    {
        DebugInformation,
        Assets,
        CrashReporting,
        SvgConversion,
        MsdfGeneration,
        OperatorMigration,
    }

    private Categories _activeCategory;

    protected override void DrawContent()
    {
        ImGui.BeginChild("categories", new Vector2(160 * T3Ui.UiScaleFactor, -1), true, ImGuiWindowFlags.NoScrollbar);
        {
            ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0, 0.5f));
            FormInputs.AddSegmentedButtonWithLabel(ref _activeCategory, "", 150 * T3Ui.UiScaleFactor);
            ImGui.PopStyleVar();
        }
        ImGui.EndChild();

        ImGui.SameLine();
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(20, 5));
        ImGui.BeginChild("content", new Vector2(-1, -1), true);
        {
            FormInputs.SetIndentToParameters();
            switch (_activeCategory)
            {
                case Categories.DebugInformation:
                    FormInputs.AddSectionHeader("Debug information");

                    if (ImGui.TreeNode("Undo history"))
                    {
                        var index = 0;
                        foreach (var c in UndoRedoStack.UndoStack)
                        {
                            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f / (index + 1) + 0.5f));
                            ImGui.PushFont(index == 0 ? Fonts.FontBold : Fonts.FontNormal);
                            if (c is MacroCommand macroCommand)
                            {
                                ImGui.Selectable($"{c.Name} ({macroCommand.Count})");
                            }
                            else
                            {
                                ImGui.Selectable(c.Name);
                            }

                            ImGui.PopFont();
                            ImGui.PopStyleColor();
                            index++;
                        }

                        ImGui.TreePop();
                    }

                    var graphInfo = ProjectView.Focused;
                    
                    if (graphInfo != null && ImGui.TreeNode("Navigation history"))
                    {
                        int index = 0;
                        foreach (var c in graphInfo.NavigationHistory.GetPreviouslySelectedInstances())
                        {
                            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f / (index + 1) + 0.5f));
                            ImGui.PushFont(index == 0 ? Fonts.FontBold : Fonts.FontNormal);
                            ImGui.Selectable($" {c})");
                            ImGui.PopFont();
                            ImGui.PopStyleColor();
                            index++;
                        }

                        ImGui.TreePop();
                    }

                    break;

                case Categories.Assets:
                    FormInputs.AddSectionHeader("File Assets");
                    FileAssetsHelper.Draw();
                    break;

                case Categories.CrashReporting:
                {
                    FormInputs.AddSectionHeader("Crash reporting");
                    CustomComponents.HelpText("Yes. This can be useful.");
                    FormInputs.SetIndentToLeft();
                    FormInputs.ApplyIndent();
                    if (ImGui.Button("Simulate crash"))
                    {
                        _crashUnlocked = !_crashUnlocked;
                    }

                    if (_crashUnlocked)
                    {
                        ImGui.SameLine();
                        ImGui.PushStyleColor(ImGuiCol.Button, UiColors.StatusWarning.Rgba);
                        ImGui.PushStyleColor(ImGuiCol.ButtonActive, UiColors.StatusWarning.Rgba);
                        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UiColors.StatusWarning.Rgba);
                        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.ForegroundFull.Rgba);
                        var crashNow = ImGui.Button("Crash now.");
                        ImGui.PopStyleColor(4);

                        if (crashNow)
                        {
                            SimulateCrash();
                        }

                        CustomComponents.TooltipForLastItem("Clicking this button will simulate a crash.\nThis can useful to test the crash reporting dialog.");
                    }

                    break;
                }

                case Categories.SvgConversion:
                    SvgFontConversion.Draw();
                    break;

                case Categories.MsdfGeneration:
                    MsdfGeneration.Draw();
                    break;

                case Categories.OperatorMigration:
                    OperatorFormatMigrationHelper.Draw();
                    break;
            }

            ImGui.EndChild();
            ImGui.PopStyleVar();
        }
    }



    private static void SimulateCrash()
    {
        throw new SimulatedCrashException();
    }

    internal override List<Window> GetInstances()
    {
        return new List<Window>();
    }
}