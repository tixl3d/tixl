#nullable enable

using ImGuiNET;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.InputUi.CombinedInputs;
using T3.Editor.Gui.Interaction;
using T3.Editor.UiModel;
using T3.Editor.UiModel.InputsAndTypes;
using T3.Serialization;

namespace T3.Editor.Gui.InputUi.VectorInputs;

internal sealed class Vector4InputUi : FloatVectorInputValueUi<Vector4>
{
    public Vector4InputUi() : base(4)
    {
        Min = 0;
        Max = 1;
        ClampMin = true;
        ClampMax = false;
    }
        
    public override IInputUi Clone()
    {
        return CloneWithType<Vector4InputUi>();
    }

    public override void ApplyValueToAnimation(IInputSlot inputSlot, InputValue inputValue, Animator animator, double time)
    {
        if (inputValue is not InputValue<Vector4> typedInputValue)
            return;

        if (!animator.TryGetCurvesForInputSlot(inputSlot, out var curves))
        {
            Log.Warning("Can't find Vec4 animation curve?");
            return;
        }
        
        typedInputValue.Value.CopyTo(FloatComponents);
        Curve.UpdateCurveValues(curves, time, FloatComponents);
    }

    public override InputEditStateFlags DrawParameterEdit(IInputSlot inputSlot, SymbolUi compositionUi, SymbolUi.Child symbolChildUi,
                                                         bool hideNonEssentials, bool skipIfDefault)
    {
        var previousTimeInBars = _adsrTimeInBars;
        _adsrTimeInBars = false;
        if (UseVec4Control == Vec4Controls.AdsrEnvelope)
        {
            foreach (var sibling in inputSlot.Parent.Inputs)
            {
                if (sibling.Id != _adsrUnitsId || sibling is not InputSlot<int> units)
                    continue;

                _adsrTimeInBars = units.GetCurrentValue() != 1;
                break;
            }
        }

        try
        {
            return base.DrawParameterEdit(inputSlot, compositionUi, symbolChildUi, hideNonEssentials, skipIfDefault);
        }
        finally
        {
            _adsrTimeInBars = previousTimeInBars;
        }
    }

    protected override InputEditStateFlags DrawEditControl(string name, Symbol.Child.Input input, ref Vector4 float4Value, bool readOnly)
    {
        if (UseVec4Control == Vec4Controls.AdsrEnvelope)
        {
            if (readOnly)
            {
                var tempEnvelope = float4Value;
                AdsrEnvelopeInputUi.DrawAdsrControl(ref tempEnvelope, cloneIfModified: false, timeInBars: _adsrTimeInBars);
                return InputEditStateFlags.Nothing;
            }

            return AdsrEnvelopeInputUi.DrawAdsrControl(ref float4Value, input.IsDefault, timeInBars: _adsrTimeInBars);
        }
        
        float4Value.CopyTo(FloatComponents);
        var thumbWidth = ImGui.GetFrameHeight();
        var inputEditState = VectorValueEdit.Draw(FloatComponents, Min, Max, Scale, ClampMin, ClampMax, thumbWidth+1);
            
        ImGui.SameLine();
        if (!readOnly)
        {
            float4Value = new Vector4(FloatComponents[0], 
                                      FloatComponents[1],
                                      FloatComponents[2],
                                      FloatComponents[3]);
        }

        if (readOnly)
        {
            var tempConstant = float4Value;
            ColorEditButton.Draw(ref tempConstant, Vector2.Zero);
            return InputEditStateFlags.Nothing;
        }
            
        var result = ColorEditButton.Draw(ref float4Value, Vector2.Zero);
            
        if (result != InputEditStateFlags.Nothing)
        {
            float4Value.CopyTo(FloatComponents);
            inputEditState |= result;
        }
        return inputEditState;
    }

    private static readonly float[] _floatComponentsForEdit = new float[4];
    private static readonly Guid _adsrUnitsId = new("d9a48d43-124d-4928-9c32-57a7d09d0ec6");
    private bool _adsrTimeInBars;

    public static InputEditStateFlags DrawColorInput(ref Vector4 float4Value, bool readOnly, float rightPadding =0)
    {
        float4Value.CopyTo(_floatComponentsForEdit);
        
        var inputEditState = VectorValueEdit.Draw(_floatComponentsForEdit, 0, 1, 0.01f, false, false, rightPadding);
            
        ImGui.SameLine();
        if (!readOnly)
        {
            float4Value = new Vector4(_floatComponentsForEdit[0], 
                                      _floatComponentsForEdit[1],
                                      _floatComponentsForEdit[2],
                                      _floatComponentsForEdit[3]);
        }

        if (readOnly)
        {
            var tempConstant = float4Value;
            ColorEditButton.Draw(ref tempConstant, Vector2.Zero);
            return InputEditStateFlags.Nothing;
        }
            
        var result = ColorEditButton.Draw(ref float4Value, Vector2.Zero);
            
        if (result != InputEditStateFlags.Nothing)
        {
            float4Value.CopyTo(_floatComponentsForEdit);
            inputEditState |= result;
        }
        return inputEditState;
    }

    public override bool DrawSettings()
    {
        var modified = base.DrawSettings();

        FormInputs.DrawFieldSetHeader("Show 4D Control");
        var tmpForRef = UseVec4Control;
        if (FormInputs.AddEnumDropdown(ref tmpForRef, null))
        {
            modified = true;
            UseVec4Control = tmpForRef;
        }

        return modified;
    }

    public override void Write(JsonTextWriter writer)
    {
        base.Write(writer);

        if (UseVec4Control != Vec4Controls.None)
            writer.WriteObject(nameof(UseVec4Control), UseVec4Control.ToString());
    }

    public override void Read(JToken? inputToken)
    {
        base.Read(inputToken);
        UseVec4Control = JsonUtils.ReadEnum<Vec4Controls>(inputToken, nameof(UseVec4Control));
    }

    public Vec4Controls UseVec4Control;

    public enum Vec4Controls
    {
        None,
        AdsrEnvelope,
    }
}
