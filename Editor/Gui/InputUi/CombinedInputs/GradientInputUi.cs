using ImGuiNET;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
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

        var size = new Vector2(ImGui.GetContentRegionAvail().X - GradientEditor.StepHandleSize.X, 
                               ImGui.GetFrameHeight());
        var area = new ImRect(ImGui.GetCursorScreenPos() + new Vector2(GradientEditor.StepHandleSize.X * 0.5f,0), 
                              ImGui.GetCursorScreenPos() + size);
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