using ImGuiNET;
using T3.Editor.Gui;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Skills.Data;
using T3.Editor.Skills.Training;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Skills.Ui;

/// <summary>
/// A dialog that is shown after level completion.
/// </summary>
internal static class SkillMapPopup
{
    internal static void Show()
    {
        _showNextFrame = true;
    }

    internal static void Draw()
    {
        if (_showNextFrame)
        {
            ImGui.OpenPopup(MapPopupId);
            _open = true;
            _showNextFrame = false;
        }

        var popUpSize = new Vector2(800, 600) * T3Ui.UiScaleFactor;

        // Center the popup in the main viewport
        var vp = ImGui.GetMainViewport();
        var pos = vp.Pos + (vp.Size - popUpSize) * 0.5f;
        ImGui.SetNextWindowSize(popUpSize, ImGuiCond.Appearing);
        ImGui.SetNextWindowPos(pos, ImGuiCond.Appearing);

        ImGui.PushStyleColor(ImGuiCol.PopupBg, UiColors.BackgroundFull.Rgba);

        if (ImGui.BeginPopupModal(MapPopupId, ref _open, ImGuiWindowFlags.None))
        {
            if (ImGui.IsWindowAppearing())
            {
                if (!SkillTraining.TryGetActiveTopicAndLevel(out var topic, out _))
                {
                    ImGui.End();
                    return;
                }

                _topicSelection.Clear();
                _topicSelection.Add(topic);
                _mapCanvas.FocusTopics(_topicSelection, 400);
            }
            
            ImGui.BeginChild("Map", new Vector2(0, -10), false, ImGuiWindowFlags.NoBackground);
            {
                _topicSelection ??= [];
                _mapCanvas.DrawContent(HandleTopicInteraction, out _, _topicSelection);
            
            }
            
            ImGui.EndChild();
            
            if (UiHelpers.IsClickedOutsideWindow())
            {
                Log.Debug("Closed?!");
                _open = false;
            }
        }

        ImGui.End();

        ImGui.PopStyleColor();
    }

    private static void HandleTopicInteraction(QuestTopic topic, bool isSelected)
    {
        if (!ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            return;

        if (!topic.IsPlayable())
            return;

        SkillProgress.Data.ActiveTopicId = topic.Id;
        SkillTraining.UpdateTopicStatesAndProgression();
        SkillTraining.StartActiveLevel();
    }

    private static bool IsPlayable(this QuestTopic  topic)
    {
        return topic.ProgressionState 
                   is QuestTopic.ProgressStates.Unlocked
                   or QuestTopic.ProgressStates.Locked
                   or QuestTopic.ProgressStates.Active 
                   or QuestTopic.ProgressStates.Passed 
                   or QuestTopic.ProgressStates.Completed 
                   or QuestTopic.ProgressStates.Started;
    }

    private static bool _showNextFrame;
    private static HashSet<QuestTopic> _topicSelection = [];
    private const string MapPopupId = "Skill Quest Overview";
    private static readonly SkillMapCanvas _mapCanvas = new();
    private static bool _open;
}