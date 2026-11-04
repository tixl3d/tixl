using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;
using T3.Editor.Gui.Windows.Layouts;
using Xunit;

namespace Core.Tests;

public class ImGuiLayoutScalingTests
{
    [Fact]
    public void SkillQuestLayout_OnWideDisplay_KeepsOutputProportion()
    {
        var (ini, titles) = LoadLayout("layout12.json");

        var result = ImGuiLayoutScaling.Rescale(ini, new Vector2(0, 26), new Vector2(2560, 1414), titles, MinPanelSize);

        // Saved on a 1706px dock space: output 739 of 1704 available pixels.
        var outputSize = ReadWindowRect(result, "Output View##0").Size;
        Assert.Equal(MathF.Round(2558 * 739f / 1704), outputSize.X);
        Assert.Equal(1414, outputSize.Y);

        var graph = ReadWindowRect(result, "Graph View##1");
        var parameters = ReadWindowRect(result, "Parameters##1");
        Assert.Equal(MathF.Round((2558 - outputSize.X - 2) * 639f / 963), graph.Size.X);
        Assert.Equal(graph.Pos.X + graph.Size.X + 2, parameters.Pos.X);
        Assert.Equal(parameters.Pos.X + parameters.Size.X + 2, ReadWindowRect(result, "Output View##0").Pos.X);
    }

    [Fact]
    public void DefaultLayout_AtSavedSize_KeepsWindowRects()
    {
        var (ini, titles) = LoadLayout("layout0.json");

        var result = ImGuiLayoutScaling.Rescale(ini, new Vector2(0, 26), new Vector2(2560, 1391), titles, MinPanelSize);

        foreach (var title in titles)
        {
            Assert.Equal(ReadWindowRect(ini, title), ReadWindowRect(result, title));
        }
    }

    [Fact]
    public void DefaultLayout_OnBiggerDisplay_ScalesAllPanels()
    {
        var (ini, titles) = LoadLayout("layout0.json");
        const float factor = 1.5f;

        var result = ImGuiLayoutScaling.Rescale(ini, new Vector2(0, 26), new Vector2(2560 * factor, 1391 * factor), titles, MinPanelSize);

        foreach (var title in titles)
        {
            var saved = ReadWindowRect(ini, title).Size;
            var scaled = ReadWindowRect(result, title).Size;
            Assert.InRange(scaled.X, saved.X * factor - 3, saved.X * factor + 3);
            Assert.InRange(scaled.Y, saved.Y * factor - 3, saved.Y * factor + 3);
        }
    }

    [Fact]
    public void DefaultLayout_OnSmallDisplay_ClampsNarrowPanels()
    {
        var (ini, titles) = LoadLayout("layout0.json");

        var result = ImGuiLayoutScaling.Rescale(ini, new Vector2(0, 20), new Vector2(800, 600), titles, MinPanelSize);

        // The library column (199 of 2558) would be 62px wide; it takes the minimum from its larger sibling.
        var library = ReadWindowRect(result, "Symbol Library");
        Assert.Equal(MinPanelSize, library.Size.X);
        Assert.Equal(MinPanelSize + 2, ReadWindowRect(result, "Graph View##1").Pos.X);
    }

    [Fact]
    public void SkillQuestLayout_IgnoresNodesWithoutVisibleWindows()
    {
        var (ini, titles) = LoadLayout("layout12.json");

        var result = ImGuiLayoutScaling.Rescale(ini, new Vector2(0, 26), new Vector2(2560, 1414), titles, MinPanelSize);

        // The library column and the empty slot below the output are pruned by ImGui, so they keep their values
        // and the graph starts at the left edge.
        foreach (var id in new[] { "0x0000000B", "0x0000000D", "0x0000000E", "0x00000010" })
        {
            Assert.Equal(FindDockLine(ini, id), FindDockLine(result, id));
        }

        Assert.Equal(0, ReadWindowRect(result, "Graph View##1").Pos.X);
        Assert.Equal(2560, ReadWindowRect(result, "Output View##0").Pos.X + ReadWindowRect(result, "Output View##0").Size.X);
    }

    [Theory]
    [InlineData("layout0.json")]
    [InlineData("layout12.json")]
    public void Rescale_KeepsAllOtherKeys(string fileName)
    {
        var (ini, titles) = LoadLayout(fileName);

        var result = ImGuiLayoutScaling.Rescale(ini, new Vector2(0, 30), new Vector2(1920, 1050), titles, MinPanelSize);

        Assert.NotEqual(ini, result);
        Assert.Equal(StripSizes(ini), StripSizes(result));
    }

    [Theory]
    [InlineData("layout0.json")]
    [InlineData("layout12.json")]
    public void Rescale_IsIdempotent(string fileName)
    {
        var (ini, titles) = LoadLayout(fileName);
        var pos = new Vector2(0, 26);
        var size = new Vector2(1920, 1054);

        var once = ImGuiLayoutScaling.Rescale(ini, pos, size, titles, MinPanelSize);
        var twice = ImGuiLayoutScaling.Rescale(once, pos, size, titles, MinPanelSize);

        Assert.Equal(once, twice);
    }

    [Fact]
    public void Rescale_WithSourceSize_UsesItInsteadOfStoredDockSpaceSize()
    {
        var (ini, titles) = LoadLayout("layout12.json");
        var pos = new Vector2(0, 26);
        var target = new Vector2(2560, 1414);

        // The live ini after a resize already stores the new dock space size, but its splits were made for the old one.
        var live = ini.Replace("Pos=0,26 Size=1706,918", "Pos=0,26 Size=2560,1414");

        var fromSaved = ImGuiLayoutScaling.Rescale(ini, pos, target, titles, MinPanelSize);
        var fromLive = ImGuiLayoutScaling.Rescale(live, pos, target, titles, MinPanelSize, sourceSize: new Vector2(1706, 918));

        Assert.Equal(fromSaved, fromLive);
    }

    [Fact]
    public void FloatingWindow_ScalesWithDockSpaceAndStaysInside()
    {
        const string ini = """
                           [Window][Floating]
                           Pos=900,500
                           Size=200,100
                           Collapsed=0

                           [Docking][Data]
                           DockSpace ID=0x08BD597D Window=0x1BBC0F80 Pos=0,0 Size=1000,600 CentralNode=1
                           """;
        var titles = new HashSet<string> { "Floating" };

        var result = ImGuiLayoutScaling.Rescale(ini, Vector2.Zero, new Vector2(500, 300), titles, MinPanelSize);

        var rect = ReadWindowRect(result, "Floating");
        Assert.Equal(new Vector2(100, 50), rect.Size);
        Assert.Equal(new Vector2(400, 250), rect.Pos);
    }

    [Fact]
    public void TryReadDockSpaceSize_ReturnsStoredSize()
    {
        var (ini, _) = LoadLayout("layout12.json");

        Assert.True(ImGuiLayoutScaling.TryReadDockSpaceSize(ini, 0x08BD597D, out var size));
        Assert.Equal(new Vector2(1706, 918), size);
        Assert.False(ImGuiLayoutScaling.TryReadDockSpaceSize(ini, 0x12345678, out _));
    }

    private static (string Ini, HashSet<string> VisibleTitles) LoadLayout(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Layouts", fileName);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var titles = new HashSet<string>();
        foreach (var config in document.RootElement.GetProperty("WindowConfigs").EnumerateArray())
        {
            if (config.GetProperty("Visible").GetBoolean())
                titles.Add(config.GetProperty("Title").GetString()!);
        }

        return (document.RootElement.GetProperty("ImGuiSettings").GetString()!, titles);
    }

    private static (Vector2 Pos, Vector2 Size) ReadWindowRect(string ini, string title)
    {
        var lines = ini.Split('\n');
        var start = Array.IndexOf(lines, $"[Window][{title}]");
        Assert.True(start >= 0, $"Window {title} not found");

        Vector2 pos = default, size = default;
        for (var index = start + 1; index < lines.Length && !lines[index].StartsWith('['); index++)
        {
            if (lines[index].StartsWith("Pos="))
                pos = ParsePair(lines[index][4..]);
            else if (lines[index].StartsWith("Size="))
                size = ParsePair(lines[index][5..]);
        }

        return (pos, size);
    }

    private static Vector2 ParsePair(string text)
    {
        var parts = text.Split(',');
        return new Vector2(float.Parse(parts[0]), float.Parse(parts[1]));
    }

    private static string FindDockLine(string ini, string id)
    {
        return ini.Split('\n').Single(line => line.Contains($" ID={id} "));
    }

    private static string StripSizes(string ini)
    {
        return Regex.Replace(ini, @"(^|\s)(Pos|Size|SizeRef)=-?\d+,-?\d+", "$1$2=", RegexOptions.Multiline);
    }

    private const float MinPanelSize = 120;
}
