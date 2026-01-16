using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.Layouts;

namespace T3.Editor.Skills.Training;

internal static class SkillTrainingStates
{
    internal static State<SkillTrainingContext> Inactive
        = new(
              Enter: context =>
                     {
                         if (context.PreviousUiState != null)
                             UiConfig.ApplyUiState(context.PreviousUiState);
                     },
              Update: context => { },
              Exit:
              _ => { }
             );

    internal static State<SkillTrainingContext> Playing
        = new(
              Enter: context =>
                     {

                     },
              
              Update: context => { },
              Exit: _ => { }
             );

    internal static State<SkillTrainingContext> Completed
        = new(
              Enter: _ => { },
              Update: context => { },
              Exit: _ => { }
             );
}