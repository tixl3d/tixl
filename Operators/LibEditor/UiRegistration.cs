using Lib.render.analyze;
using Lib.numbers.anim._obsolete;
using Lib.numbers.color;
using Lib.numbers.data.utils;
using Lib.flow.context;
using Lib.io.audio;
using Lib.io.midi;
using Lib.numbers.anim.animators;
using Lib.numbers.@bool.logic;
using Lib.numbers.curve;
using Lib.numbers.@float.adjust;
using Lib.Utils;
using libEditor.CustomUi;
using T3.Core.Logging;
using T3.Editor.Compilation;
using T3.Editor.Gui.ChildUi;
using T3.Editor.Gui.Interaction.Timing;
using T3.Editor.Gui.Templates;
using Types.Collections;
using Types.Values;
using Boolean = Types.Values.Boolean;
using String = Types.Values.String;

namespace libEditor;

public class UiRegistration : IEditorUiExtension
{
    // ReSharper disable once EmptyConstructor
    public UiRegistration()
    {
    }

    public void Initialize()
    {
        CustomChildUiRegistry.Register(typeof(Counter), CounterUi.DrawChildUi, _types);
        CustomChildUiRegistry.Register(typeof(SampleGradient), GradientSliderUi.DrawChildUi, _types);
        CustomChildUiRegistry.Register(typeof(SampleCurve), SampleCurveUi.DrawChildUi, _types);
        CustomChildUiRegistry.Register(typeof(_Jitter), _JitterUi.DrawChildUi, _types);
        CustomChildUiRegistry.Register(typeof(_Jitter2d), Jitter2dUi.DrawChildUi, _types);
        CustomChildUiRegistry.Register(typeof(String), StringUi.DrawChildUi, _types);
        CustomChildUiRegistry.Register(typeof(Value), ValueUi.DrawChildUi, _types);
        CustomChildUiRegistry.Register(typeof(IntValue), IntValueUi.DrawChildUi, _types);
        CustomChildUiRegistry.Register(typeof(Remap), RemapUi.DrawChildUi, _types);
        CustomChildUiRegistry.Register(typeof(AnimValue), AnimValueUi.DrawChildUi, _types);
        CustomChildUiRegistry.Register(typeof(AnimVec2), AnimVec2Ui.DrawChildUi, _types);
        CustomChildUiRegistry.Register(typeof(AnimVec3), AnimVec3Ui.DrawChildUi, _types);
        CustomChildUiRegistry.Register(typeof(SequenceAnim), SequenceAnimUi.DrawChildUi, _types);
        CustomChildUiRegistry.Register(typeof(TriggerAnim), TriggerAnimUi.DrawChildUi, _types);
        CustomChildUiRegistry.Register(typeof(MidiInput), MidiInputUi.DrawChildUi, _types);
        CustomChildUiRegistry.Register(typeof(Boolean), BooleanUi.DrawChildUi, _types);
        CustomChildUiRegistry.Register(typeof(Trigger), TriggerUi.DrawChildUi, _types);

        CustomChildUiRegistry.Register(typeof(GetIntVar), GetIntVarUi.DrawChildUi, _types);
        CustomChildUiRegistry.Register(typeof(GetFloatVar), GetFloatVarUi.DrawChildUi, _types);
        CustomChildUiRegistry.Register(typeof(SetFloatVar), SetFloatVarUi.DrawChildUi, _types);

        CustomChildUiRegistry.Register(typeof(AudioReaction), AudioReactionUi.DrawChildUi, _types);
        CustomChildUiRegistry.Register(typeof(GpuMeasure), GpuMeasureUi.DrawChildUi, _types);
        CustomChildUiRegistry.Register(typeof(DataList), DataListUi.DrawChildUi, _types);
        
        //CustomChildUiRegistry.Register(typeof(SelectFloatFromDict), SelectFloatFromDictUi.DrawChildUi, _types);
        CustomChildUiRegistry.Register(typeof(SelectVec2FromDict), SelectVec2FromDictUi.DrawChildUi, _types);
        CustomChildUiRegistry.Register(typeof(SelectBoolFromFloatDict), SelectBoolFromFloatDictUi.DrawChildUi, _types);

        PlaybackUtils.BpmProvider = BpmProvider.Instance;
        PlaybackUtils.TapProvider = TapProvider.Instance;

        foreach (var templateDefinition in TemplateDefinitions.Templates)
        {
            TemplateDefinition.AddTemplateDefinition(templateDefinition);
        }
        
        Log.Debug("Registered UI Entries");
    }

    public void Uninitialize()
    {
        foreach(var templateDefinition in TemplateDefinitions.Templates)
        {
            TemplateDefinition.RemoveTemplateDefinition(templateDefinition);
        }
        
        foreach (var type in _types.ToArray())
        {
            CustomChildUiRegistry.Remove(type, _types);
        }
        
        Log.Debug("Unregistered UI Entries");
    }
    
    private readonly List<Type> _types = new();
}