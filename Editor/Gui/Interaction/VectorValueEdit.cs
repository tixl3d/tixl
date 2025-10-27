using ImGuiNET;
using T3.Editor.Gui.Styling;
using T3.Editor.UiModel.InputsAndTypes;

namespace T3.Editor.Gui.Interaction;

internal static class VectorValueEdit
{
    // Float control
    internal static InputEditStateFlags Draw(float[] components, float min, float max, float scale, bool clampMin, bool clampMax, float rightPadding = 0,
                                             string format = null)
    {
        var width = (ImGui.GetContentRegionAvail().X - rightPadding) / components.Length - 1;
        var size = new Vector2(width, 0);

        var resultingEditState = InputEditStateFlags.Nothing;
        var hasButtons = components.Length == 1 && format?.EndsWith("°") == true;

        // Calculate sizes for float buttons
        var buttonSize = hasButtons ? Vector2.One * ImGui.GetFrameHeight() : Vector2.Zero;
        var fieldWidth = hasButtons ? width - buttonSize.X * 2 : width;
        var fieldSize = new Vector2(fieldWidth, 0);

        for (var index = 0; index < components.Length; index++)
        {
            if (index > 0)
                ImGui.SameLine();

            ImGui.PushID(index);

            if (hasButtons)
            {
                // Draw the input field with adjusted size for buttons
                resultingEditState |= SingleValueEdit.Draw(ref components[index], fieldSize, min, max, clampMin, clampMax, scale, format ??= "{0:0.000}");

                // Draw +/- buttons for single float component with ° format
                var increment = ImGui.GetIO().KeyShift ? SHIFT_INCREMENT_FLOAT : DEFAULT_INCREMENT_FLOAT;
                ImGui.SameLine();
                ImGui.PushFont(Icons.IconFont);
                if (DrawButton((char)Icon.RotateCounterClockwise+"", buttonSize, !clampMin || components[index] > min))
                {
                    components[index] -= increment;
                    if (clampMin)
                        components[index] = Math.Max(min, components[index]);
                    resultingEditState |= InputEditStateFlags.ModifiedAndFinished;
                }

                ImGui.SameLine();
                if (DrawButton((char)Icon.RotateClockwise + "", buttonSize, !clampMax || components[index] < max))
                {
                    components[index] += increment;
                    if (clampMax)
                        components[index] = Math.Min(max, components[index]);
                    resultingEditState |= InputEditStateFlags.ModifiedAndFinished;
                }
                ImGui.PopFont();
            }
            else
            {
                // Draw without buttons
                resultingEditState |= SingleValueEdit.Draw(ref components[index], size, min, max, clampMin, clampMax, scale, format ??= "{0:0.000}");
            }
            
            ImGui.PopID();
        }

        return resultingEditState;
    }

    /// <summary>
    /// Integer control, with optional +/- buttons if only one component
    /// </summary>
    internal static InputEditStateFlags Draw(int[] components, int min, int max, float scale, bool clampMin, bool clampMax)
    {
        var resultingEditState = InputEditStateFlags.Nothing;
        var hasButtons = components.Length == 1;

        // Calculate sizes
        var width = ImGui.GetContentRegionAvail().X / components.Length - 1;
        var buttonSize = hasButtons ? Vector2.One * ImGui.GetFrameHeight() : Vector2.Zero;
        var fieldWidth = hasButtons ? width - buttonSize.X * 2 : width;
        var size = new Vector2(fieldWidth, 0);

        for (var index = 0; index < components.Length; index++)
        {
            if (index > 0)
                ImGui.SameLine();

            ImGui.PushID(index);

            // Draw the input field
            resultingEditState |= SingleValueEdit.Draw(ref components[index], size, min, max, clampMin, clampMax);

            // Draw +/- buttons for single component
            if (hasButtons)
            {
                var increment = ImGui.GetIO().KeyShift ? SHIFT_INCREMENT : 1;
                ImGui.SameLine();
                if (DrawButton("-", buttonSize, !clampMin || components[index] > min))
                {
                    components[index] -= increment;
                    if (clampMin)
                        components[index] = Math.Max(min, components[index]);
                    resultingEditState |= InputEditStateFlags.ModifiedAndFinished;
                }

                ImGui.SameLine();
                if (DrawButton("+", buttonSize, !clampMax || components[index] < max))
                {
                    components[index] += increment;
                    if (clampMax)
                        components[index] = Math.Min(max, components[index]);
                    resultingEditState |= InputEditStateFlags.ModifiedAndFinished;
                }
            }

            ImGui.PopID();
        }

        return resultingEditState;
    }

    private static bool DrawButton(string label, Vector2 size, bool enabled)
    {
        if (!enabled)
            ImGui.BeginDisabled();

        var result = ImGui.Button(label, size);

        if (!enabled)
            ImGui.EndDisabled();

        return result;
    }

    private const int SHIFT_INCREMENT = 10;
    private const float DEFAULT_INCREMENT_FLOAT = 90.0f;
    private const float SHIFT_INCREMENT_FLOAT = 45.0f;
}