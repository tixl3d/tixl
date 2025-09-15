using ImGuiNET;
using T3.Editor.UiModel.InputsAndTypes;

namespace T3.Editor.Gui.Interaction;

internal static class VectorValueEdit
{
    // Float control
    internal static InputEditStateFlags Draw(float[] components, float min, float max, float scale, bool clampMin, bool clampMax, float rightPadding = 0,
                                             string format = null)
    {
        var width = (ImGui.GetContentRegionAvail().X - rightPadding) / components.Length-1;
        var size = new Vector2(width, 0);

        var resultingEditState = InputEditStateFlags.Nothing;
        for (var index = 0; index < components.Length; index++)
        {
            if (index > 0)
                ImGui.SameLine();

            ImGui.PushID(index);
            resultingEditState |= SingleValueEdit.Draw(ref components[index], size, min, max, clampMin, clampMax, scale,  format??="{0:0.000}");
            ImGui.PopID();
        }

        return resultingEditState;
    }
        
    // Integer control
    internal static InputEditStateFlags Draw(int[] components, int min, int max, float scale, bool clampMin, bool clampMax)
    {
        var buttonsize = Vector2.One * ImGui.GetFrameHeight();

        var componentsCount = components.Length;

        var resultingEditState = InputEditStateFlags.Nothing;

        if (componentsCount == 1)
        {
            var width = ImGui.GetContentRegionAvail().X / components.Length - 1;
            width -= buttonsize.X * 2; // for the buttons
            var size = new Vector2(width, 0); 

            for (var index = 0; index < components.Length; index++)
            {
                if (index > 0)
               
                ImGui.SameLine();
                ImGui.PushID(index);
                resultingEditState |= SingleValueEdit.Draw(ref components[index], size, min, max, clampMin, clampMax);
                ImGui.PopID();

                ImGui.SameLine();
                if (ImGui.Button("-", buttonsize))
                {
                    if (ImGui.GetIO().KeyShift)
                        components[index] -= 10;
                    else
                        components[index]--;

                    resultingEditState |= InputEditStateFlags.ModifiedAndFinished;

                }

                ImGui.SameLine();
                if (ImGui.Button("+", buttonsize))
                {
                    if (ImGui.GetIO().KeyShift)
                        components[index] += 10;
                    else
                        components[index]++;

                    resultingEditState |= InputEditStateFlags.ModifiedAndFinished;
                }

            }
        }
        else
        {
            var width = ImGui.GetContentRegionAvail().X / components.Length - 1;
            
            var size = new Vector2(width, 0);
            for (var index = 0; index < components.Length; index++)
            {
                if (index > 0)
                
                ImGui.SameLine();

                ImGui.PushID(index);
                resultingEditState |= SingleValueEdit.Draw(ref components[index], size, min, max, clampMin, clampMax);
                ImGui.PopID();
            }
        }

        return resultingEditState;
    }
}