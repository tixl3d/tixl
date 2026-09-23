using ImGuiNET;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Core.Animation;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.UiModel.InputsAndTypes;
using T3.Serialization;

namespace T3.Editor.Gui.InputUi.SingleControl;

public sealed class BoolInputUi : SingleControlInputUi<bool>
{
    public override bool IsAnimatable => true;

    public enum UsageType
    {
        CheckBox,
        Trigger,
    }
    public UsageType Usage { get; private set; } = UsageType.CheckBox;

    /// <summary>Whether the momentary trigger button was held during the previous frame.</summary>
    private bool _wasHeld;

    public override IInputUi Clone()
    {
        return new BoolInputUi()
                   {
                       InputDefinition = InputDefinition,
                       Parent = Parent,
                       PosOnCanvas = PosOnCanvas,
                       Relevancy = Relevancy,
                       Usage = Usage,
        };
    }

    protected override bool DrawSingleEditControl(string name, ref bool value)
    {
        var size = Vector2.One * ImGui.GetFrameHeight();
        switch (Usage)
        {
            case UsageType.CheckBox:
             return ImGui.Checkbox("##boolParam", ref value);
                
            case UsageType.Trigger:
                ImGui.Button("##boolParam", size);
                var isHeld = ImGui.IsItemActive();
                if (isHeld)
                {
                    _wasHeld = true;
                    value = true;
                    return true;
                }

                if (_wasHeld)
                {
                    _wasHeld = false;
                    value = false;
                    return true; 
                }

                return false;
                
            default:
                return false;
        }
     
    }

    public override bool DrawSettings()
    {
        var modified = base.DrawSettings();
        FormInputs.AddVerticalSpace();

        FormInputs.DrawFieldSetHeader("Usage");
        {
            var tmpForRef = Usage;
            if (FormInputs.AddEnumDropdown(ref tmpForRef, null))
            {
                modified = true;
                Usage = tmpForRef;
            }
        }

        return modified;
    }

    public override void Write(JsonTextWriter writer)
    {
        base.Write(writer);

        if (Usage != UsageType.CheckBox)
            writer.WriteObject(nameof(Usage), Usage.ToString());
    }

    public override void Read(JToken? inputToken)
    {
        if (inputToken == null)
            return;

        base.Read(inputToken);

        var usageToken = inputToken[nameof(Usage)];
        if (usageToken == null)
            return;

        var usageName = usageToken.Value<string>();

        // "Default" was this enum member's name before it was renamed to "CheckBox".
        if (usageName == "Default")
        {
            Usage = UsageType.CheckBox;
            return;
        }

        if (Enum.TryParse<UsageType>(usageName, out var usageValue))
        {
            Usage = usageValue;
        }
    }

    protected override void DrawReadOnlyControl(string name, ref bool value)
    {
        ImGui.TextUnformatted(value.ToString());
    }

    protected override InputEditStateFlags DrawAnimatedValue(string name, InputSlot<bool> inputSlot, Animator animator)
    {
        var time = Playback.Current.TimeInBars;
        if (!animator.TryGetCurvesForInputSlot(inputSlot, out var curves)
            || curves.Length != 1)
        {
            Log.Assert($"Animated bool requires a singe animation curve.");
            return InputEditStateFlags.Nothing;
        }

        var curve = curves[0];
        var value = curve.GetSampledValue(time) > 0.5f;

        var modified = DrawSingleEditControl(name, ref value);
        if (!modified)
            return InputEditStateFlags.Nothing;

        inputSlot.SetTypedInputValue(value);

        return InputEditStateFlags.ModifiedAndFinished;
    }

    public override void ApplyValueToAnimation(IInputSlot inputSlot, InputValue inputValue, Animator animator, double time)
    {
        if (inputValue is not InputValue<bool> boolInputValue)
            return;

        var value = boolInputValue.Value;

        if (!animator.TryGetCurvesForInputSlot(inputSlot, out var curves)
            || curves.Length != 1)
        {
            Log.Error("Expected 1 curve for bool animation");
            return;
        }

        Curve.UpdateCurveBoolValue(curves[0], time, value);
    }
}