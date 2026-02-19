using ImGuiNET;
using T3.Core.Audio;
using T3.Core.IO;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.InputsAndTypes;

namespace T3.Editor.Gui.Windows;

internal sealed partial class SettingsWindow
{
    private static float _smoothedGlobalLevel;
    private static float _smoothedOperatorLevel;
    private static float _smoothedSoundtrackLevel;
    
    private static float EdgePadding => 10 * T3Ui.UiScaleFactor;

    private static void DrawAudioPanel(ref bool changed)
    {
        FormInputs.AddSectionHeader("Audio System");
        FormInputs.AddVerticalSpace();
        
        changed |= DrawMixerSection(
            "Global Mixer",
            ref ProjectSettings.Config.GlobalPlaybackVolume,
            0.0f, 1.0f,
            ProjectSettings.Defaults.GlobalPlaybackVolume,
            "Affects all audio output at the global mixer level.",
            AudioMixerManager.GetGlobalMixerLevel(),
            ref _smoothedGlobalLevel,
            ref ProjectSettings.Config.GlobalMute,
            ProjectSettings.Defaults.GlobalMute,
            "Mute all audio output at the global mixer level.");
        AudioEngine.SetGlobalMute(ProjectSettings.Config.GlobalMute);
        
        changed |= DrawMixerSection(
            "Operator Mixer",
            ref ProjectSettings.Config.OperatorPlaybackVolume,
            0.0f, 1.0f,
            ProjectSettings.Defaults.OperatorPlaybackVolume,
            "Affects all operator audio output at the operator mixer level.",
            AudioMixerManager.GetOperatorMixerLevel(),
            ref _smoothedOperatorLevel,
            ref ProjectSettings.Config.OperatorMute,
            ProjectSettings.Defaults.OperatorMute,
            "Mute all operator audio output at the operator mixer level.");
        AudioMixerManager.SetOperatorMixerVolume(ProjectSettings.Config.OperatorPlaybackVolume);
        AudioEngine.SetOperatorMute(ProjectSettings.Config.OperatorMute);
        
        changed |= DrawMixerSection(
            "Soundtrack Mixer",
            ref ProjectSettings.Config.SoundtrackPlaybackVolume,
            0.0f, 10f,
            ProjectSettings.Defaults.SoundtrackPlaybackVolume,
            "Limit the audio playback volume for the soundtrack",
            AudioMixerManager.GetSoundtrackMixerLevel(),
            ref _smoothedSoundtrackLevel,
            ref ProjectSettings.Config.SoundtrackMute,
            ProjectSettings.Defaults.SoundtrackMute,
            "Mute soundtrack audio only.");
    }
    
    /// <summary>
    /// Draws a mixer section with volume slider, level meter, and mute controls.
    /// </summary>
    private static bool DrawMixerSection(
        string sectionLabel,
        ref float volume,
        float minVolume,
        float maxVolume,
        float defaultVolume,
        string volumeTooltip,
        float currentLevel,
        ref float smoothedLevel,
        ref bool mute,
        bool defaultMute,
        string muteTooltip)
    {
        var changed = false;
        
        ImGui.PushID(sectionLabel);
        
        DrawSectionHeader(sectionLabel);
        
        ImGui.SetCursorPosX(EdgePadding);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Volume");
        CustomComponents.TooltipForLastItem(volumeTooltip);
        
        ImGui.SameLine(0, 10 * T3Ui.UiScaleFactor);
        
        var resetButtonSize = ImGui.GetFrameHeight();
        var muteTextWidth = ImGui.CalcTextSize("Mute").X + 30 * T3Ui.UiScaleFactor + resetButtonSize;
        var sliderWidth = 80 * T3Ui.UiScaleFactor;
        
        ImGui.SetNextItemWidth(sliderWidth);
        if (SingleValueEdit.Draw(ref volume, new Vector2(sliderWidth, ImGui.GetFrameHeight()), minVolume, maxVolume, true, true) != InputEditStateFlags.Nothing)
        {
            changed = true;
        }
        
        changed |= DrawResetButton(ref volume, defaultVolume, "Reset to default");
        
        var contentMaxX = ImGui.GetContentRegionMax().X;
        ImGui.SameLine(contentMaxX - muteTextWidth - EdgePadding);
        if (ImGui.Checkbox("Mute", ref mute))
        {
            changed = true;
        }
        CustomComponents.TooltipForLastItem(muteTooltip);
        
        ImGui.SameLine();
        ImGui.PushID("muteReset");
        changed |= DrawResetButton(ref mute, defaultMute, "Reset to default");
        ImGui.PopID();
        
        var windowPos = ImGui.GetWindowPos();
        var meterLeftEdge = windowPos.X + ImGui.GetWindowContentRegionMin().X + EdgePadding;
        var meterRightEdge = windowPos.X + ImGui.GetWindowContentRegionMax().X - EdgePadding;
        
        AudioLevelMeter.DrawAbsoluteWithinBounds("", currentLevel, ref smoothedLevel, 2f, meterLeftEdge, meterRightEdge);
        
        DrawSectionSeparator();
        
        ImGui.PopID();
        
        return changed;
    }
    
    private static void DrawSectionHeader(string label)
    {
        FormInputs.AddVerticalSpace(3);
        ImGui.PushFont(Fonts.FontSmall);
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        ImGui.SetCursorPosX(EdgePadding);
        ImGui.TextUnformatted(label.ToUpperInvariant());
        ImGui.PopStyleColor();
        ImGui.PopFont();
        FormInputs.AddVerticalSpace(2);
    }
    
    private static void DrawSectionSeparator()
    {
        FormInputs.AddVerticalSpace(6);
        var p = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X - 2 * EdgePadding;
        ImGui.GetWindowDrawList().AddRectFilled(
            p + new Vector2(EdgePadding, 0),
            p + new Vector2(EdgePadding + width, 1),
            UiColors.ForegroundFull.Fade(0.05f));
        FormInputs.AddVerticalSpace(6);
    }
    
    private static bool DrawResetButton<T>(ref T value, T defaultValue, string tooltip) where T : IEquatable<T>
    {
        ImGui.SameLine();
        var isModified = !value.Equals(defaultValue);
        ImGui.PushStyleColor(ImGuiCol.Text, isModified ? UiColors.TextMuted.Rgba : UiColors.TextMuted.Fade(0.3f).Rgba);
        var clicked = CustomComponents.IconButton(Icon.Revert, new Vector2(ImGui.GetFrameHeight()));
        ImGui.PopStyleColor();
        CustomComponents.TooltipForLastItem(tooltip);
        
        if (clicked)
        {
            value = defaultValue;
            return true;
        }
        return false;
    }
}
