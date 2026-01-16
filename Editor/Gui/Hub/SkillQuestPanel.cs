#nullable enable

using ImGuiNET;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Window;
using T3.Editor.Skills.Data;
using T3.Editor.Skills.Ui;
using SkillTraining = T3.Editor.Skills.Training.SkillTraining;

namespace T3.Editor.Gui.Hub;

internal static class SkillQuestPanel
{
    internal static void Draw(GraphWindow window, bool projectViewJustClosed)
    {
        SkillTraining.SetGraphWindow(window);
        
        if (!UserSettings.Config.ShowSkillQuestInHub)
            return;
        
        if (!SkillTraining.TryGetActiveTopicAndLevel(out var activeTopic, out var activeLevel))
        {
            ImGui.TextUnformatted("no skill quest data");
            return;
        }
        
        if (projectViewJustClosed || NeedsUpdate)
        {
            SkillProgressionUi.TopicSelection.Clear();
            if (activeTopic.ProgressionState == QuestTopic.ProgressStates.Completed)
            {
                foreach (var topic in SkillMapData.Data.Topics)
                {
                    if (topic.ProgressionState 
                        is QuestTopic.ProgressStates.Completed 
                        or QuestTopic.ProgressStates.Unlocked 
                        or QuestTopic.ProgressStates.Passed)
                        SkillProgressionUi.TopicSelection.Add(topic);
                }
            }
            else
            {
                SkillProgressionUi.TopicSelection.Add(activeTopic);
            }
            _mapCanvas.FocusTopics(SkillProgressionUi.TopicSelection);
            
            // Only selected active
            SkillProgressionUi.TopicSelection.Clear();
            SkillProgressionUi.TopicSelection.Add(activeTopic);
            NeedsUpdate = false;
        }
        
        ContentPanel.Begin("Skill Quest", 
                           "An interactive journey from playful TiXL basics to advanced real-time graphics design.", 
                           DrawPanelActions, Height);
        
        SkillProgressionUi.DrawContent(activeTopic, 
                                       activeLevel, 
                                       SkillProgressionUi.ContentModes.HubPanel, 
                                       ()=>
                                       {
                                           SkillTraining.StartActiveLevel(forceSaveUiState:true);
                                       });
        ContentPanel.End();
    }


    
    private static void DrawPanelActions()
    {
        CustomComponents.RightAlign(ImGui.GetFrameHeight());
        if (CustomComponents.TransparentIconButton(Icon.Cross, Vector2.Zero))
        {
            UserSettings.Config.ShowSkillQuestInHub = false;
            UserSettings.Save();
        }
        CustomComponents.TooltipForLastItem("""
                                            Hides Skill Quest panel. 
                                            You can enable it again in the Settings.
                                            """);
        
    }

    internal static float Height => 230 * T3Ui.UiScaleFactor;
    private static readonly SkillMapCanvas _mapCanvas = new();
    public static bool NeedsUpdate;
}