#nullable enable
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Window;
using T3.Editor.Gui.Windows.Layouts;
using T3.Editor.Skills.Data;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Skills.Training;

internal sealed class SkillTrainingContext // can't be generic because it's used for generic state
{
    internal OpenedProject? OpenedProject;
    internal UiConfig.UiElementsVisibility? PreviousUiState;
    internal ProjectView? ProjectView;
    internal required StateMachine<SkillTrainingContext> StateMachine;
    public QuestLevel? ActiveLevel;
    public QuestTopic? ActiveTopic;
    public GraphWindow? GraphWindow;
}