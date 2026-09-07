#nullable enable
using System.Runtime.CompilerServices;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.OutputUi;

internal sealed class VectorOutputUi<T> : OutputUi<T>
{
    public override IOutputUi Clone()
    {
        return new VectorOutputUi<T>
                   {
                       OutputDefinition = OutputDefinition,
                       PosOnCanvas = PosOnCanvas,
                       Size = Size
                   };
    }

    protected override void DrawTypedValue(ISlot slot, string viewId)
    {
        if (slot is not Slot<T> typedSlot)
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
            settings.CurveCanvas.Reset(value);
        }
        settings.CurveCanvas.Draw(value);
    }

    private sealed class ViewSettings
    {
        public readonly VectorCurvePlotCanvas<T> CurveCanvas = new();
        public required ISlot CurrentSlot;
    }

    private static readonly ConditionalWeakTable<string, ViewSettings> _viewSettings = [];
}