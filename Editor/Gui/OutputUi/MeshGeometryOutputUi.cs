#nullable enable
using System.Diagnostics;
using ImGuiNET;
using T3.Core.DataTypes;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using Color = T3.Core.DataTypes.Vector.Color;

namespace T3.Editor.Gui.OutputUi;

/// <summary>
/// Output view for CPU geometry: a summary of counts, bounds, volume, watertightness
/// and the attribute table. The text is rebuilt only when the geometry changes.
/// </summary>
internal sealed class MeshGeometryOutputUi : OutputUi<MeshGeometry>
{
    public override IOutputUi Clone()
    {
        return new MeshGeometryOutputUi
                   {
                       OutputDefinition = OutputDefinition,
                       PosOnCanvas = PosOnCanvas,
                       Size = Size
                   };
    }

    protected override void DrawTypedValue(ISlot slot, string viewId)
    {
        if (slot is not Slot<MeshGeometry> typedSlot)
        {
            Debug.Assert(false);
            return;
        }

        var geometry = typedSlot.Value;
        if (geometry == null)
        {
            _lastGeometry = null;
            CustomComponents.EmptyWindowMessage("No geometry");
            return;
        }

        if (_stats.UpdateIfChanged(geometry) || !ReferenceEquals(geometry, _lastGeometry))
        {
            _lastGeometry = geometry;
            RebuildLines(geometry);
        }

        ImGui.BeginChild("GeometryStats");
        ImGui.Indent(10 * T3Ui.UiScaleFactor);
        FormInputs.AddVerticalSpace(5);

        CustomComponents.StylizedText(_headline, Fonts.FontLarge, UiColors.Text);
        FormInputs.AddVerticalSpace(3);

        for (var i = 0; i < _lineCount; i++)
        {
            CustomComponents.StylizedText(_labels[i], Fonts.FontSmall, UiColors.TextMuted);
            ImGui.SameLine(LabelWidth * T3Ui.UiScaleFactor);
            CustomComponents.StylizedText(_values[i], Fonts.FontNormal, _valueColors[i]);
        }

        if (_attributeCount > 0)
        {
            FormInputs.AddVerticalSpace(8);
            CustomComponents.StylizedText("Attributes", Fonts.FontSmall, UiColors.TextMuted);
            for (var i = 0; i < _attributeCount; i++)
            {
                CustomComponents.StylizedText(_attributeNames[i], Fonts.FontNormal, UiColors.Text);
                ImGui.SameLine(LabelWidth * T3Ui.UiScaleFactor);
                CustomComponents.StylizedText(_attributeInfos[i], Fonts.FontSmall, UiColors.TextMuted);
            }
        }

        if (_partRowCount > 0)
        {
            FormInputs.AddVerticalSpace(8);
            CustomComponents.StylizedText("Parts", Fonts.FontSmall, UiColors.TextMuted);
            DrawPartTable();
        }

        ImGui.Unindent(10 * T3Ui.UiScaleFactor);
        ImGui.EndChild();
    }

    private void DrawPartTable()
    {
        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingFixedFit;
        var height = Math.Min(_partRowCount + 1, MaxVisiblePartRows) * ImGui.GetTextLineHeightWithSpacing();
        if (!ImGui.BeginTable("Parts", 6, flags, new Vector2(0, height)))
            return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("#");
        ImGui.TableSetupColumn("Faces");
        ImGui.TableSetupColumn("Open Edges");
        ImGui.TableSetupColumn("Volume");
        ImGui.TableSetupColumn("Seed");
        ImGui.TableSetupColumn("Pivot");
        ImGui.TableHeadersRow();

        unsafe
        {
            var clipperData = new ImGuiListClipper();
            var clipper = new ImGuiListClipperPtr(&clipperData);
            clipper.Begin(_partRowCount, ImGui.GetTextLineHeightWithSpacing());
            while (clipper.Step())
            {
                for (var row = clipper.DisplayStart; row < clipper.DisplayEnd; row++)
                {
                    DrawPartRow(row);
                }
            }

            clipper.End();
        }

        ImGui.EndTable();
    }

    private void DrawPartRow(int row)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(_partIndexTexts[row]);
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(_partFaceTexts[row]);
        ImGui.TableNextColumn();
        if (_partOpenEdgeTexts[row].Length == 0)
        {
            CustomComponents.StylizedText("-", Fonts.FontNormal, UiColors.TextMuted);
        }
        else
        {
            CustomComponents.StylizedText(_partOpenEdgeTexts[row], Fonts.FontNormal, UiColors.StatusAttention);
        }

        ImGui.TableNextColumn();
        ImGui.TextUnformatted(_partVolumeTexts[row]);
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(_partSeedTexts[row]);
        ImGui.TableNextColumn();
        CustomComponents.StylizedText(_partPivotTexts[row], Fonts.FontSmall, UiColors.TextMuted);
    }

    private void RebuildLines(MeshGeometry geometry)
    {
        var s = _stats;
        _headline = s.PartCount > 1
                        ? $"{s.FaceCount:N0} faces in {s.PartCount:N0} parts"
                        : $"{s.FaceCount:N0} faces";

        _lineCount = 0;
        AddLine("Points", $"{s.PointCount:N0}", UiColors.Text);
        AddLine("Triangles", $"{s.TriangleCount:N0}", UiColors.Text);
        AddLine("Size", $"{s.Size.X:0.###} x {s.Size.Y:0.###} x {s.Size.Z:0.###}", UiColors.Text);
        AddLine("Bounds", $"({s.BoundsMin.X:0.###}, {s.BoundsMin.Y:0.###}, {s.BoundsMin.Z:0.###}) .. ({s.BoundsMax.X:0.###}, {s.BoundsMax.Y:0.###}, {s.BoundsMax.Z:0.###})",
                UiColors.Text);
        AddLine("Volume", $"{s.Volume:0.####}", UiColors.Text);

        if (s.FaceCount == 0)
        {
            AddLine("Surface", "point cloud", UiColors.TextMuted);
        }
        else if (s.BoundaryEdges == 0 && s.NonManifoldEdges == 0)
        {
            AddLine("Surface", "watertight", UiColors.StatusControlled);
        }
        else
        {
            var info = s.NonManifoldEdges > 0
                           ? $"{s.BoundaryEdges:N0} boundary edges, {s.NonManifoldEdges:N0} non-manifold"
                           : $"{s.BoundaryEdges:N0} boundary edges (open)";
            AddLine("Surface", info, UiColors.StatusAttention);
        }

        RebuildPartRows();

        _attributeCount = 0;
        foreach (var attribute in geometry.Attributes)
        {
            if (_attributeCount == _attributeNames.Length)
                break;

            _attributeNames[_attributeCount] = attribute.Name;
            _attributeInfos[_attributeCount] = $"{TypeName(attribute)} per {attribute.Domain}";
            _attributeCount++;
        }
    }

    /// <summary>Row strings are formatted once per geometry change so the table draw stays allocation-free.</summary>
    private void RebuildPartRows()
    {
        var parts = _stats.Parts;
        _partRowCount = parts.Length;
        if (_partIndexTexts.Length < _partRowCount)
        {
            _partIndexTexts = new string[_partRowCount];
            _partFaceTexts = new string[_partRowCount];
            _partOpenEdgeTexts = new string[_partRowCount];
            _partVolumeTexts = new string[_partRowCount];
            _partSeedTexts = new string[_partRowCount];
            _partPivotTexts = new string[_partRowCount];
        }

        for (var i = 0; i < _partRowCount; i++)
        {
            var part = parts[i];
            _partIndexTexts[i] = i.ToString();
            _partFaceTexts[i] = part.FaceCount.ToString("N0");
            _partOpenEdgeTexts[i] = part.BoundaryEdges == 0 ? string.Empty : part.BoundaryEdges.ToString("N0");
            _partVolumeTexts[i] = part.Volume.ToString("0.####");
            _partSeedTexts[i] = part.SeedIndex.ToString();
            _partPivotTexts[i] = $"{part.Pivot.X:0.###}, {part.Pivot.Y:0.###}, {part.Pivot.Z:0.###}";
        }
    }

    private void AddLine(string label, string value, Color color)
    {
        if (_lineCount == _labels.Length)
            return;

        _labels[_lineCount] = label;
        _values[_lineCount] = value;
        _valueColors[_lineCount] = color;
        _lineCount++;
    }

    private static string TypeName(GeometryAttribute attribute)
    {
        return attribute switch
                   {
                       GeometryAttribute<float>   => "float",
                       GeometryAttribute<int>     => "int",
                       GeometryAttribute<Vector2> => "vec2",
                       GeometryAttribute<Vector3> => "vec3",
                       GeometryAttribute<Vector4> => "vec4",
                       _                          => "?",
                   };
    }

    private const float LabelWidth = 90;
    private const int MaxLines = 8;
    private const int MaxAttributes = 32;
    private const int MaxVisiblePartRows = 24;

    private int _partRowCount;
    private string[] _partIndexTexts = [];
    private string[] _partFaceTexts = [];
    private string[] _partOpenEdgeTexts = [];
    private string[] _partVolumeTexts = [];
    private string[] _partSeedTexts = [];
    private string[] _partPivotTexts = [];

    private readonly MeshGeometryStats _stats = new();
    private MeshGeometry? _lastGeometry;
    private string _headline = string.Empty;
    private readonly string[] _labels = new string[MaxLines];
    private readonly string[] _values = new string[MaxLines];
    private readonly Color[] _valueColors = new Color[MaxLines];
    private int _lineCount;
    private readonly string[] _attributeNames = new string[MaxAttributes];
    private readonly string[] _attributeInfos = new string[MaxAttributes];
    private int _attributeCount;
}
