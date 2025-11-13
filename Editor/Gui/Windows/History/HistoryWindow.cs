using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using T3.Editor.UiModel.Commands;

namespace T3.Editor.Gui.Windows.History;

internal sealed class HistoryWindow : Window
{
    internal HistoryWindow()
    {
        Config.Title = "History";
    }

    private int selectedIndex = 0;

    internal override List<Window> GetInstances() => [];

    protected override void DrawContent()
    {
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new System.Numerics.Vector4(0, 0, 0, 0));
        if (ImGui.BeginListBox("##History", new System.Numerics.Vector2(-1, -1)))
        {
            var undoList = UndoRedoStack.UndoStack.ToList();
            for (int i = 0; i < undoList.Count; i++)
            {
                bool selected = selectedIndex == i;

                var element = undoList[i];
                ImGui.Text(undoList[i].Name);
                //ImGui.Selectable(element.ToString() + "##" + element.uid.ToString(), ref selected);
                if (selected)
                {
                    selectedIndex = i;
                }
            }

            ImGui.EndListBox();
        }
        ImGui.PopStyleColor();
    }
}

