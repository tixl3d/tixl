using ImGuiNET;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Editor.Gui.Interaction;
using T3.Editor.UiModel.InputsAndTypes;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.InputUi.CombinedInputs;

public sealed class CurveInputUi : InputValueUi<Curve>
{
    public override IInputUi Clone()
    {
        return new CurveInputUi
                   {
                       InputDefinition = InputDefinition,
                       Parent = Parent,
                       PosOnCanvas = PosOnCanvas,
                       Relevancy = Relevancy,
                       Size = Size,
                   };
    }
        
    protected override InputEditStateFlags DrawEditControl(string name, Symbol.Child.Input input, ref Curve curve, bool readOnly)
    {
        var comp = ProjectView.Focused?.CompositionInstance;
        if (comp == null)
            return InputEditStateFlags.Nothing;
            
        if (curve == null)
        {
            // value was null!
            ImGui.TextUnformatted(name + " is null?!");
            return InputEditStateFlags.Nothing;
        }
            
        ImGui.Dummy(Vector2.One);    // Add Line Break

        var keepPositionForIcon = ImGui.GetCursorPos();
            
        var cloneIfModified = input.IsDefault;
        var modified= CurveInputEditing.DrawCanvasForCurve(ref curve, input, cloneIfModified, comp, T3Ui.EditingFlags.PreventZoomWithMouseWheel);

        if (CurveEditPopup.DrawPopupIndicator(comp, input, ref curve, keepPositionForIcon, cloneIfModified, out var popupResult))
        {
            modified = popupResult;
        }

        if (cloneIfModified && (modified & InputEditStateFlags.Modified) != InputEditStateFlags.Nothing)
        {
            input.IsDefault = false;
        } 
        return modified;
    }
        
    protected override void DrawReadOnlyControl(string name, ref Curve value)
    {
    }
}