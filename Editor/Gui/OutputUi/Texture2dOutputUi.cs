using System.Diagnostics;
using ImGuiNET;
using T3.Core.DataTypes;
using T3.Core.Operator.Slots;
using T3.Editor.App;
using T3.Editor.Gui.Windows;

namespace T3.Editor.Gui.OutputUi;

internal sealed class Texture2dOutputUi : OutputUi<Texture2D>
{
    public override IOutputUi Clone()
    {
        return new Texture2dOutputUi()
                   {
                       OutputDefinition = OutputDefinition,
                       PosOnCanvas = PosOnCanvas,
                       Size = Size
                   };
    }

    protected override void DrawTypedValue(ISlot slot, string viewId)
    {
        if (slot is Slot<Texture2D> typedSlot)
        {
            var texture = typedSlot.Value;
            if (texture == null || texture.IsDisposed)
                return;

            try
            {
                if (texture.Description.ArraySize > 1)
                {
                    ImGui.TextUnformatted("Array-Size: " + texture.Description.ArraySize);
                        
                }
            }   
            catch(Exception e)
            {
                Log.Warning("Failed to access texture description:" + e.Message);
            }
            ImageOutputCanvas.Current.DrawTexture(texture);
            ProgramWindows.Viewer?.SetTexture(texture);
        }
        else
        {
            Debug.Assert(false);
        }
    }
}