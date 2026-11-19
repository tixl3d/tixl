using ImGuiNET;
using T3.Editor.Gui;
using T3.Editor.Gui.Input;
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
            var skipContent = false;
            if (ImGui.IsWindowAppearing())
            {
                if (!SkillTraining.TryGetActiveTopicAndLevel(out var topic, out _))
                {
                    skipContent = true;
                }
                else
                {
                    _topicSelection.Clear();
                    _topicSelection.Add(topic);
                    _mapCanvas.FocusTopics(_topicSelection, 400);
                }
            }

            if (!skipContent)
            {
                DrawFilterOptions();

                ImGui.BeginChild("Map", new Vector2(0, -10), ImGuiChildFlags.None, ImGuiWindowFlags.NoBackground);
                {
                    _topicSelection ??= [];
                    ApplyFilterSettings(_mapCanvas);
                    _mapCanvas.DrawContent(HandleTopicInteraction, out _, _topicSelection);
                }
                ImGui.EndChild();

                if (UiHelpers.IsClickedOutsideWindow())
                {
                    Log.Debug("Closed?!");
                    _open = false;
                }
            }

            ImGui.EndPopup();
        }

        ImGui.PopStyleColor();
    }

    /** Keeps every player-facing map in sync with the filter options of the overview. */
    internal static void ApplyFilterSettings(SkillMapCanvas canvas)
    {
        canvas.ShowPlannedTopics = UserSettings.Config.SkillMapShowPlannedTopics;
        canvas.HighlightNewTopics = UserSettings.Config.SkillMapHighlightNewTopics;
    }

    private static void DrawFilterOptions()
    {
        var changed = FormInputs.AddCheckBox("Highlight New",
                                             ref UserSettings.Config.SkillMapHighlightNewTopics,
                                             "Fades topics without levels you have never played.",
                                             isFlushLeft: true);
        ImGui.SameLine(0, 20 * T3Ui.UiScaleFactor);
        changed |= FormInputs.AddCheckBox("Planned",
                                          ref UserSettings.Config.SkillMapShowPlannedTopics,
                                          "Shows topics whose levels are still planned or in draft.",
                                          isFlushLeft: true);
        if (changed)
            UserSettings.Save();
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