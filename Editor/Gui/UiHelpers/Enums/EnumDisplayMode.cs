#nullable enable
using ImGuiNET;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.UiModel.InputsAndTypes;
using T3.Serialization;

namespace T3.Editor.Gui.UiHelpers.Enums;

/// <summary>
/// How an enum parameter is drawn. Curated per input in the parameter settings ("Display as") and saved to the .t3ui.
/// </summary>
public enum EnumDisplayMode
{
    Dropdown,

    /// <summary>
    /// All options side by side, for a few well established choices like None / F1 / F2.
    /// Falls back to <see cref="Dropdown"/> when the options don't fit the available width.
    /// </summary>
    SegmentedSwitch,
}

/// <summary>Settings, serialization and drawing of <see cref="EnumDisplayMode"/>, shared by enum and mapped int inputs.</summary>
internal static class EnumDisplayModeUi
{
    public static bool DrawSettings(ref EnumDisplayMode displayMode)
    {
        FormInputs.AddVerticalSpace();
        FormInputs.DrawFieldSetHeader("Display as");
        return FormInputs.AddEnumDropdown(ref displayMode, null);
    }

    public static void Write(JsonTextWriter writer, EnumDisplayMode displayMode)
    {
        if (displayMode != EnumDisplayMode.Dropdown)
            writer.WriteObject(DisplayModeKey, displayMode.ToString());
    }

    public static EnumDisplayMode Read(JToken? inputToken)
    {
        return JsonUtils.TryGetEnumValue<EnumDisplayMode>(inputToken?[DisplayModeKey], out var displayMode)
                   ? displayMode
                   : EnumDisplayMode.Dropdown;
    }

    /// <summary>
    /// Draws the options as a segmented switch if <paramref name="displayMode"/> asks for it and they fit the current item width.
    /// Returns false when the caller has to draw the dropdown instead.
    /// </summary>
    public static bool TryDrawSegmented(EnumDisplayMode displayMode, EnumCache.Entry enumInfo, ref int index, out InputEditStateFlags editStateFlags)
    {
        editStateFlags = InputEditStateFlags.Nothing;
        if (displayMode != EnumDisplayMode.SegmentedSwitch || enumInfo.IsFlagEnum)
            return false;

        var width = ImGui.CalcItemWidth();
        if (CustomComponents.GetSegmentedButtonWidth(enumInfo.DisplayNames) > width)
            return false;

        if (CustomComponents.SegmentedButton(enumInfo.DisplayNames, ref index, width, enumInfo.IsUnavailable, enumInfo.Tooltips,
                                             isParameterInput: true))
            editStateFlags = InputEditStateFlags.ModifiedAndFinished;

        return true;
    }

    private const string DisplayModeKey = "DisplayAs";
}
