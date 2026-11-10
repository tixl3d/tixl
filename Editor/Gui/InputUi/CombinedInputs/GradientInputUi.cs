using ImGuiNET;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.InputsAndTypes;

namespace T3.Editor.Gui.InputUi.CombinedInputs;

public sealed class GradientInputUi : InputValueUi<Gradient>
{
    public override IInputUi Clone()
    {
        return new GradientInputUi
                   {
                       InputDefinition = InputDefinition,
                       Parent = Parent,
                       PosOnCanvas = PosOnCanvas,
                       Relevancy = Relevancy,
                       Size = Size,
                   };
    }
        
    protected override InputEditStateFlags DrawEditControl(string name, Symbol.Child.Input input, ref Gradient gradient, bool readOnly)
    {
        if (gradient == null)
        {
            ImGui.TextUnformatted(name + " is null?!?");
            if (input.IsDefault)
            {
                var newGradient = new InputValue<Gradient>();
                newGradient.Value = new Gradient();
                InputDefinition.DefaultValue.Assign(newGradient);
                input.Value.Assign(newGradient);
                input.SetCurrentValueAsDefault();
            }
            return InputEditStateFlags.Nothing;
        }

        // The options icon makes the context menu discoverable; with Ctrl it skips the menu and opens the larger editor.
        // Narrow and right after the last handle's overhang, so the gradient keeps as much width as possible.
        var iconWidth = MathF.Round(Icons.FontSize + 4 * T3Ui.UiScaleFactor);
        var handleOverhang = GradientEditor.StepHandleSize.X * 0.5f;
        var origin = ImGui.GetCursorScreenPos();
        var gradientWidth = ImGui.GetContentRegionAvail().X - iconWidth - handleOverhang;
        ImGui.SetCursorScreenPos(new Vector2(origin.X + gradientWidth + handleOverhang, origin.Y));
        var opensPopup = ImGui.GetIO().KeyCtrl;
        if (CustomComponents.IconButton(opensPopup ? Icon.PopUp : Icon.Settings2, new Vector2(iconWidth, ImGui.GetFrameHeight())))
        {
            if (opensPopup)
            {
                GradientEditor.OpenPopup();
            }
            else
            {
                GradientEditor.OpenContextMenu();
            }
        }

        if (ImGui.IsItemHovered())
        {
            if (opensPopup)
            {
                CustomComponents.TooltipForLastItem("Edit gradient", "Opens a larger editor with a list of the steps.");
            }
            else
            {
                CustomComponents.TooltipForLastItem("Gradient options", "Presets, interpolation, and a larger editor. Also on right-click.\nCtrl+click opens the larger editor directly.");
            }
        }
        ImGui.SetCursorScreenPos(origin);

        var size = new Vector2(gradientWidth, ImGui.GetFrameHeight());
        var area = new ImRect(origin + new Vector2(GradientEditor.StepHandleSize.X * 0.5f, 0),
                              origin + size);
        var drawList = ImGui.GetWindowDrawList();

        var cloneIfModified = input.IsDefault;
        var modified= GradientEditor.Draw(ref gradient, drawList, area, cloneIfModified);

        if (cloneIfModified && modified.HasFlag(InputEditStateFlags.Modified))
        {
            input.IsDefault = false;
        } 
        return modified;
    }
    
    protected override void DrawReadOnlyControl(string name, ref Gradient value)
    {
        ImGui.NewLine();
    }
}