using ImGuiNET;
using T3.Editor.Gui;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Skills.Training;
using Color = T3.Core.DataTypes.Vector.Color;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Skills.Ui;

/// <summary>
/// A dialog that is shown after level completion.
/// </summary>
internal static class SkillProgressionPopup
{
    internal static void Show()
    {
        ImGui.OpenPopup(ProgressionPopupId);
        IsOpen = true;
        StarShowerEffect.Reset();
    }

    internal static void Draw()
    {
        var popUpSize = new Vector2(700, 260) * T3Ui.UiScaleFactor;

        // Center the popup in the main viewport
        var vp = ImGui.GetMainViewport();
        var pos = vp.Pos + (vp.Size - popUpSize) * 0.5f;
        ImGui.SetNextWindowSize(popUpSize, ImGuiCond.Always);
        ImGui.SetNextWindowPos(pos);

        if (!SkillTraining.TryGetActiveTopicAndLevel(out var topic, out var activeLevel))
            return;

        if (IsOpen)
        {
            ImGui.OpenPopup(ProgressionPopupId);
        }

        ImGui.PushStyleColor(ImGuiCol.PopupBg, Color.Mix(UiColors.BackgroundFull, UiColors.ForegroundFull, 0.06f).Rgba);

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10));
        if (ImGui.BeginPopupModal("ProgressionPopup", ref IsOpen,
                                  ImGuiWindowFlags.NoResize
                                  | ImGuiWindowFlags.NoMove
                                  | ImGuiWindowFlags.NoTitleBar
                                  | ImGuiWindowFlags.AlwaysUseWindowPadding
                                 ))
        {
            SkillProgressionUi.DrawContent(topic,
                                           activeLevel,
                                           SkillProgressionUi.ContentModes.PopUp,
                                           SkillTraining.CompleteAndProgressToNextLevel);

            if (UiHelpers.IsClickedOutsideWindow())
            {
                IsOpen = false;
            }

            ImGui.EndPopup();
            StarShowerEffect.DrawAndUpdate();
        }

        ImGui.PopStyleColor();
        ImGui.PopStyleVar();
    }

    private const string ProgressionPopupId = "ProgressionPopup";
    public static bool IsOpen;
}