using System.Diagnostics.CodeAnalysis;
using ImGuiNET;
using T3.Core.DataTypes.Vector;

namespace T3.Editor.Gui.UiHelpers;

        [SuppressMessage("ReSharper", "SuggestBaseTypeForParameterInConstructor")]
    public class StyleScope : IDisposable
    {
        public StyleScope(Coloring[] colors=null, Styling[] styles=null)
        {
            ApplyScope(colors, styles, out _colorCount, out _styleCount);
        }

        public static void ApplyScope(Coloring[] colors, Styling[] styles, out int colorCount, out int styleCount)
        {
            colorCount = 0;
            if (colors?.Length > 0)
            {
                foreach(var color in colors)
                {
                    ImGui.PushStyleColor(color.Id, color.Color.Rgba);
                }
                colorCount = colors.Length;
            }

            styleCount = 0;
            if (styles?.Length > 0)
            {
                foreach(var s in styles)
                {
                    switch (s.Value)
                    {
                        case float f:
                            ImGui.PushStyleVar(s.Id, f);
                            break;
                        case Vector2 v:
                            ImGui.PushStyleVar(s.Id, v);
                            break;
                    }
                }
                styleCount = styles.Length;
            }
            else
            {
                styleCount = 0;
            }
        }
        

        public void Dispose()
        {
            ImGui.PopStyleColor(_colorCount);
            ImGui.PopStyleVar(_styleCount);
        }

        private int _colorCount;
        private int _styleCount;
    }

    public sealed class ChildWindowScope : IDisposable
    {
        public ChildWindowScope(string name, Vector2 size,  ImGuiWindowFlags flags, Color backgroundColor, int padding =0, int rounding=0)
        {
            ImGui.PushStyleColor(ImGuiCol.ChildBg, backgroundColor.Rgba);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(padding,padding));
            ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, rounding);
            ImGui.BeginChild(name, size, ImGuiChildFlags.AlwaysUseWindowPadding, flags);
        }
        
        public void Dispose()
        {
            ImGui.EndChild();
            ImGui.PopStyleColor(1);
            ImGui.PopStyleVar(2);
        }
        
    }
    


    public  record Coloring(ImGuiCol Id, Color Color);
    public  record Styling(ImGuiStyleVar Id,  object Value);
