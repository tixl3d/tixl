using ImGuiNET;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Core.Animation;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.UiModel.InputsAndTypes;
using T3.Serialization;

namespace T3.Editor.Gui.InputUi.VectorInputs;

internal abstract class FloatVectorInputValueUi<T> : InputValueUi<T>
{
    protected FloatVectorInputValueUi(int componentCount)
    {
        FloatComponents = new float[componentCount];
    }

    public override bool IsAnimatable => true;

    protected FloatVectorInputValueUi<T> CloneWithType<TT>() where TT : FloatVectorInputValueUi<T>, new()
    {
        return new TT()
                   {
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
            || curves.Length < FloatComponents.Length)
        {
            ImGui.PushID(inputSlot.Parent.SymbolChildId.GetHashCode() + inputSlot.Id.GetHashCode());
            DrawReadOnlyControl(name, ref inputSlot.Value);
            ImGui.PopID();
            return InputEditStateFlags.Nothing;
        }

        for (var index = 0; index < FloatComponents.Length; index++)
        {
            FloatComponents[index] = (float)curves[index].GetSampledValue(time);
        }

        ImGui.PushID(inputSlot.Parent.SymbolChildId.GetHashCode() + inputSlot.Id.GetHashCode());
        var inputEditState = DrawEditControl(name, inputSlot.Input, ref inputSlot.Value, false);
        ImGui.PopID();

        if ((inputEditState & InputEditStateFlags.Modified) == InputEditStateFlags.Modified)
        {
            inputSlot.SetTypedInputValue(inputSlot.Value);
        }

        return inputEditState;
    }

    protected override void DrawReadOnlyControl(string name, ref T float2Value)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.StatusAutomated.Rgba);
        DrawEditControl(name, null!, ref float2Value, true);
        ImGui.PopStyleColor();
    }

    protected override string GetSlotValueAsString(ref T float2Value)
    {
        return string.Format(T3Ui.FloatNumberFormat, float2Value);
    }

    public abstract override void ApplyValueToAnimation(IInputSlot inputSlot, InputValue inputValue, Animator animator, double time);

    private struct ValueSettingPreset
    {
        public string Title;
        public float Scale;
        public float MinValue;
        public float MaxValue;
        public string CustomFormat;
        public bool ClampRangeMin;
        public bool ClampRangeMax;
    }

    private ValueSettingPreset[] _valueSettingPresets =
        {
            new()
                {
                    Scale = 0,
                    Title = "Rotation",
                    MinValue = -180,
                    MaxValue = 180,
                    CustomFormat = "{0:0.0}°"
                },
            new()
                {
                    Scale = 0,
                    Title = "Translation",
                    MinValue = -2,
                    MaxValue = 2,
                    CustomFormat = null
                },
            new()
                {
                    Scale = 0,
                    Title = "Color",
                    MinValue = 0,
                    MaxValue = 1,
                    CustomFormat = null,
                    ClampRangeMin = true,
                },
            new()
                {
                    Scale = 0,
                    Title = "Signed Scale × Factor",
                    MinValue = -5,
                    MaxValue = 5,
                    CustomFormat = "{0:0.000} ×",
                },
            new()
                {
                    Scale = 0,
                    Title = "Scale × Factor",
                    MinValue = 0,
                    MaxValue = 5,
                    CustomFormat = "{0:0.000} ×",
                },
            new()
                {
                    Scale = 0,
                    Title = "Bias&Gain",
                    MinValue = 0,
                    MaxValue = 1,
                    CustomFormat = "{0:0.000}",
                    ClampRangeMin = true,
                    ClampRangeMax = true,
                },
        };

    public override bool DrawSettings()
    {
        var modified = false;
        var keepPosition = ImGui.GetCursorPos();
        ImGui.SetCursorPos(new Vector2(ImGui.GetWindowContentRegionMax().X - ImGui.GetFrameHeight() - ImGui.GetStyle().FramePadding.X,
                                       ImGui.GetWindowContentRegionMin().Y));

        if (CustomComponents.IconButton(Icon.Settings2, Vector2.One * ImGui.GetFrameHeight()))
        {
            ImGui.OpenPopup("customFormats");
        }

        ImGui.SetCursorPos(keepPosition);

        if (ImGui.BeginPopup("customFormats", ImGuiWindowFlags.Popup))
        {
            ImGui.TextUnformatted("Apply formatting presets...");
            foreach (var p in _valueSettingPresets)
            {
                if (!ImGui.Button(p.Title)) 
                    continue;
                
                Min = p.MinValue;
                Max = p.MaxValue;
                _scale = p.Scale;
                ClampMin = p.ClampRangeMin;
                ClampMax = p.ClampRangeMax;
                Format = p.CustomFormat;
                    
                Parent?.FlagAsModified();
                ImGui.CloseCurrentPopup();
                modified = true;
            }

            ImGui.EndPopup();
        }

        modified |= base.DrawSettings();

        FormInputs.DrawFieldSetHeader("Value Range");
        if (FormInputs.DrawValueRangeControl(ref Min, ref Max, ref _scale, ref ClampMin, ref ClampMax, DefaultMin, DefaultMax, DefaultScale))
        {
            modified = true;
        }

        FormInputs.DrawFieldSetHeader("Custom Value format");

        if (
            FormInputs.AddStringInput("##valueFormat",
                                      ref Format,
                                      "Custom format like {0:0.0}",
                                      null,
                                      "Defines custom value format. Here are some examples:\n\n" +
                                      "{0:0.00000} - High precision\n" +
                                      "{0:0}× - With a suffix\n" +
                                      "{0:G5} - scientific notation",
                                      null)
            )
        {
            modified = true;
            if (string.IsNullOrWhiteSpace(Format))
            {
                Format = null;
            }
        }

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

        if (ClampMin)
            writer.WriteValue("ClampMin", ClampMin);

        if (ClampMax)
            writer.WriteValue("ClampMax", ClampMax);
        
        if (!string.IsNullOrEmpty(Format))
            writer.WriteObject("Format", Format);

        // ReSharper enable CompareOfFloatsByEqualityOperator
    }

    public void CopyTo(FloatVectorInputValueUi<T> target)
    {
        target.Min = Min;
        target.Max = Max;
        target.Scale = Scale;
        target.ClampMin = ClampMin;
        target.ClampMax = ClampMax;
        target.Format = Format;
    }

    public override void Read(JToken inputToken)
    {
        base.Read(inputToken);
        if (inputToken == null)
            return;

        Min = inputToken["Min"]?.Value<float>() ?? DefaultMin;
        Max = inputToken["Max"]?.Value<float>() ?? DefaultMax;
        _scale = inputToken["Scale"]?.Value<float>() ?? DefaultScale;
        
        var legacyClamp = inputToken["Clamp"]?.Value<bool>() ?? false;
        ClampMin = (inputToken["ClampMin"]?.Value<bool>() ?? false) | legacyClamp;
        ClampMax = (inputToken["ClampMax"]?.Value<bool>() ?? false) | legacyClamp;
        
        
        Format = inputToken["Format"]?.Value<string>();
    }

    private static float GetScaleFromRange(float scale, float min, float max)
    {
        // Automatically set scale from range
        if (scale > 0)
            return scale;

        // A lame hack because we can't set infinity values in parameter properties
        if (min < -9999 || max > 9999)
        {
            return 0.01f;
        }

        return Math.Abs(min - max) / 100;
    }

    public float Min = DefaultMin;
    public float Max = DefaultMax;
    private float _scale = DefaultScale;
    public float Scale { 
        get => FloatInputUi.GetScaleFromRange(_scale, Min, Max);
        set => _scale = value;
    }

    //private bool Clamp;
    public bool ClampMin;
    public bool ClampMax;
    public string Format;

    protected readonly float[] FloatComponents;

    private const float DefaultScale = 0.0f;
    private const float DefaultMin = -9999999f;
    private const float DefaultMax = 9999999f;
}