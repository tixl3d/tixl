using ImGuiNET;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Core.Animation;
using T3.Serialization;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.UiModel.InputsAndTypes;

namespace T3.Editor.Gui.InputUi.VectorInputs;

internal abstract class IntVectorInputValueUi<T> : InputValueUi<T>
{
    protected IntVectorInputValueUi(int componentCount)
    {
        IntComponents = new int[componentCount];
    }
        
    public override bool IsAnimatable => true;
        
    protected IntVectorInputValueUi<T> CloneWithType<TT>() where TT : IntVectorInputValueUi<T>, new() {
        return new TT() {
                                Max = Max,
                                Min = Min,
                                _scale = _scale,
                                ClampMin = ClampMin,
                                ClampMax = ClampMax,
                                InputDefinition = InputDefinition,
                                Parent = Parent,
                                PosOnCanvas = PosOnCanvas,
                                Relevancy = Relevancy,
                                Size = Size,
                            };
    }
        
    protected override InputEditStateFlags DrawAnimatedValue(string name, InputSlot<T> inputSlot, Animator animator)
    {
        var time = Playback.Current.TimeInBars;
        if (!animator.TryGetCurvesForInputSlot(inputSlot, out var curves)
                ||curves.Length < IntComponents.Length)
        {
            ImGui.PushID(inputSlot.Parent.SymbolChildId.GetHashCode() + inputSlot.Id.GetHashCode());
            DrawReadOnlyControl(name, ref inputSlot.Value);
            ImGui.PopID();
            return InputEditStateFlags.Nothing; 
        }

        for (var index = 0; index < IntComponents.Length; index++)
        {
            IntComponents[index] = (int)(curves[index].GetSampledValue(time) + 0.5f);
        }
            
        ImGui.PushID(inputSlot.Parent.SymbolChildId.GetHashCode() + inputSlot.Id.GetHashCode());
        var inputEditState = DrawEditControl(name,inputSlot.Input, ref inputSlot.Value, false);
        ImGui.PopID();
            
        if ((inputEditState & InputEditStateFlags.Modified) == InputEditStateFlags.Modified)
        {
            inputSlot.SetTypedInputValue(inputSlot.Value);
        }
        return inputEditState;
    }
        
        
        
    protected override void DrawReadOnlyControl(string name, ref T int2Value)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.StatusAutomated.Rgba);
        DrawEditControl(name,null, ref int2Value, true);
        ImGui.PopStyleColor();
    }
        
    protected override string GetSlotValueAsString(ref T int2Value)
    {
        return string.Format(T3Ui.FloatNumberFormat, int2Value);
    }


    public abstract override void ApplyValueToAnimation(IInputSlot inputSlot, InputValue inputValue, Animator animator, double time);
        
    public override bool DrawSettings()
    {
        var modified = base.DrawSettings();
        FormInputs.DrawFieldSetHeader("Value Range");
        modified |= FormInputs.DrawIntValueRangeControl(ref Min, ref Max, ref _scale, ref ClampMin, ref ClampMax);
        return modified;
    }

    public override void Write(JsonTextWriter writer)
    {
        base.Write(writer);

        // ReSharper disable CompareOfFloatsByEqualityOperator
        if (Min != DefaultMin)
            writer.WriteValue("Min", Min);

        if (Max != DefaultMax)
            writer.WriteValue("Max", Max);

        if (_scale != DefaultScale)
            writer.WriteValue("Scale", _scale);
            
        if(ClampMin)
            writer.WriteValue("ClampMin", ClampMin);
        
        if(ClampMax)
            writer.WriteValue("ClampMax", ClampMax);

        // ReSharper enable CompareOfFloatsByEqualityOperator
    }

    public override void Read(JToken inputToken)
    {
        base.Read(inputToken);

        Min = inputToken?["Min"]?.Value<int>() ?? DefaultMin;
        Max = inputToken?["Max"]?.Value<int>() ?? DefaultMax;
        _scale = inputToken?["Scale"]?.Value<float>() ?? DefaultScale;
        
        var legacyClamp=inputToken?["Clamp"]?.Value<bool>() ?? false;
        
        ClampMin =(inputToken?["ClampMin"]?.Value<bool>() ?? false) | legacyClamp;
        ClampMax =(inputToken?["ClampMax"]?.Value<bool>() ?? false) | legacyClamp;
    }
    
    public int Min  = DefaultMin;
    public int Max  = DefaultMax;
    private float _scale = DefaultScale;
    public float Scale  => 0.1f;
    public bool ClampMin;
    public bool ClampMax;
        
    protected readonly int[] IntComponents;

    private const float DefaultScale = 0.1f;
    public const int DefaultMin = int.MinValue;
    public const int DefaultMax = int.MaxValue;        
}