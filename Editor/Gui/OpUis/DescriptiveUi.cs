using System.IO;
using ImGuiNET;
using T3.Core.Operator;
using T3.Core.Operator.Interfaces;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.OpUis.WidgetUi;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.OpUis;

internal static class DescriptiveUi
{
    internal static readonly DrawChildUiDelegate DrawChildUiDelegate = (Instance instance, ImDrawListPtr drawList, ImRect area, ScalableCanvas canvas, ref OpUiBinding data1) 
                                                                           => DrawChildUi(instance, drawList, area, canvas, ref data1);
    
    public static OpUi.CustomUiResult DrawChildUi(Instance instance, ImDrawListPtr drawList, ImRect area, ScalableCanvas canvas, ref OpUiBinding data1)
    {
        if(instance is not IDescriptiveFilename descriptiveGraphNode)
            return OpUi.CustomUiResult.None;
            
        drawList.PushClipRect(area.Min, area.Max, true);
            
        // Label if instance has title
        var symbolChild = instance.SymbolChild;
            
        WidgetElements.DrawSmallTitle(drawList, area, !string.IsNullOrEmpty(symbolChild.Name) ? symbolChild.Name : symbolChild.Symbol.Name, canvas.Scale);

        var slot = descriptiveGraphNode.SourcePathSlot;
        var xxx = slot.GetCurrentValue();
        
        var filePath = xxx != null ?  Path.GetFileName(xxx) : "";
            
        WidgetElements.DrawPrimaryValue(drawList, area, filePath, canvas.Scale);
            
        drawList.PopClipRect();
        return OpUi.CustomUiResult.Rendered | OpUi.CustomUiResult.PreventInputLabels | OpUi.CustomUiResult.AllowThumbnail;
    }
}