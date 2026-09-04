#nullable enable

using System.Runtime.CompilerServices;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.OutputUi;

internal sealed class BoolOutputUi : OutputUi<bool>
{
    public override IOutputUi Clone()
    {
        return new BoolOutputUi
                   {
                       OutputDefinition = OutputDefinition,
                       PosOnCanvas = PosOnCanvas,
                       Size = Size
                   };
    }
        
    protected override void DrawTypedValue(ISlot slot, string viewId)
    {
        if (slot is not Slot<bool> typedSlot)
            return;
     
        if (!_viewSettings.TryGetValue(viewId, out var settings))
        {
            settings = new ViewSettings
                           {
                               CurrentSlot = typedSlot
                           };
            _viewSettings.Add(viewId,settings);
        }
        
        var value = typedSlot.Value;
            
        if (slot != settings.CurrentSlot)
        {
            settings.CurrentSlot = slot;
            settings.CurveCanvas.Reset(value?1:0);
        }
        settings.CurveCanvas.Draw(value?1:0);
    }

    private sealed class ViewSettings
    {
        public readonly CurvePlotCanvas CurveCanvas = new() ;
        public required ISlot CurrentSlot;
    }
    
    private static readonly ConditionalWeakTable<string, ViewSettings> _viewSettings = [];
}