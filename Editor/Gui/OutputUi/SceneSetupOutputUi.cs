using ImGuiNET;
using T3.Core.DataTypes;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;

namespace T3.Editor.Gui.OutputUi;

internal sealed class SceneSetupOutputUi : OutputUi<float>
{
    public override IOutputUi Clone()
    {
        return new SceneSetupOutputUi()
                   {
                       OutputDefinition = OutputDefinition,
                       PosOnCanvas = PosOnCanvas,
                       Size = Size
                   };
    }
    
    protected override void DrawTypedValue(ISlot slot, string viewId)
    {
        if (slot is not Slot<SceneSetup> sceneSetupSlot)
            return;

        var setup = sceneSetupSlot.Value;
        if (setup == null)
        {
            ImGui.TextUnformatted("Undefined setup");
            return;
        }

        if (setup.RootNodes == null)
        {
            ImGui.TextUnformatted("node structure undefined");
            return;
            
        }

        FormInputs.AddSectionHeader("Structure");

        foreach (var node in setup.RootNodes)
        {
            DrawNode(node);
        }
    }
    
    private void DrawNode(SceneSetup.SceneNode node)
    {
        var label = string.IsNullOrEmpty(node.Name) ? "???" : node.Name;
        //ImGui.SetNextItemOpen(true);
        
        var isOpen = ImGui.TreeNodeEx(label,ImGuiTreeNodeFlags.DefaultOpen);
        if(isOpen)
        {
            var meshLabel = string.IsNullOrEmpty(node.MeshName) ? "-" : "Mesh:" + node.MeshName;
            ImGui.SameLine(200);
            ImGui.TextUnformatted(meshLabel);
            
            foreach (var child in node.ChildNodes)
            {
                DrawNode(child);
            }
            ImGui.TreePop();
        }
    }
}