using System.Numerics;
using Lib.Utils;
using Xunit;

namespace Core.Tests;

/// <summary>
/// Unit tests for the CSV rules used by <c>CsvToPoints</c>. The parser is compiled into this
/// assembly as linked source (see Core.Tests.csproj) so it can be tested without a GPU device
/// or an evaluation context.
/// </summary>
public class CsvPointParserTests
{
    [Fact]
    public void Parse_SplitsPlainRows()
    {
        var rows = CsvPointParser.Parse("x,y,z\n1,2,3\n4,5,6\n");

        Assert.Equal(3, rows.Count);
        Assert.Equal(new[] { "x", "y", "z" }, rows[0]);
        Assert.Equal(new[] { "1", "2", "3" }, rows[1]);
        Assert.Equal(new[] { "4", "5", "6" }, rows[2]);
    }

    [Fact]
    public void Parse_HandlesQuotesCommasAndEscapedQuotes()
    {
        var rows = CsvPointParser.Parse("name,pos\n\"fixture, left\",\"1.5\"\n\"a \"\"b\"\"\",2\n");

        Assert.Equal(3, rows.Count);
        Assert.Equal("fixture, left", rows[1][0]);
        Assert.Equal("1.5", rows[1][1]);
        Assert.Equal("a \"b\"", rows[2][0]);
    }

    [Fact]
    public void Parse_SupportsQuotedLineBreaks()
    {
        var rows = CsvPointParser.Parse("a,b\n\"line1\nline2\",2\n");

        Assert.Equal(2, rows.Count);
        Assert.Equal("line1\nline2", rows[1][0]);
        Assert.Equal("2", rows[1][1]);
    }

    [Fact]
    public void Parse_HandlesCrLfAndSkipsBlankLines()
    {
        var rows = CsvPointParser.Parse("x,y\r\n\r\n1,2\r\n   \r\n3,4");

        Assert.Equal(3, rows.Count);
        Assert.Equal(new[] { "1", "2" }, rows[1]);
        Assert.Equal(new[] { "3", "4" }, rows[2]);
    }

    [Fact]
    public void Parse_StopsAtRowBudget()
    {
        var rows = CsvPointParser.Parse("h\n1\n2\n3\n4\n", 3, out var exceeded);

        Assert.True(exceeded);
        Assert.Equal(3, rows.Count);
    }

    [Fact]
    public void Parse_DoesNotReportFalseOverflow()
    {
        var rows = CsvPointParser.Parse("h\n1\n2\n", 3, out var exceeded);

        Assert.False(exceeded);
        Assert.Equal(3, rows.Count);
    }

    [Fact]
    public void NormalizeHeader_StripsBomAndWhitespace()
    {
        Assert.Equal("Position X", CsvPointParser.NormalizeHeader("\uFEFF Position X "));
        Assert.Equal("Position X", CsvPointParser.NormalizeHeader("  Position X  "));
    }

    [Theory]
    [InlineData("1.5", 1.5f)]
    [InlineData("-2.25m", -2.25f)]
    [InlineData("26.8°", 26.8f)]
    [InlineData("300W", 300f)]
    [InlineData("50%", 50f)]
    [InlineData("1e-3", 0.001f)]
    [InlineData("  42  ", 42f)]
    public void TryParseNumber_AcceptsNumbersWithUnitSuffixes(string raw, float expected)
    {
        Assert.True(CsvPointParser.TryParseNumber(raw, out var value));
        Assert.Equal(expected, value, 4);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void TryParseNumber_RejectsInvalidOrNonFiniteCells(string raw)
    {
        Assert.False(CsvPointParser.TryParseNumber(raw, out _));
    }

    [Fact]
    public void GetFloat_FallsBackForUnmappedShortAndInvalidCells()
    {
        var row = new[] { "1.25", "oops" };

        Assert.Equal(1.25f, CsvPointParser.GetFloat(row, 0, 7f), 4);
        Assert.Equal(7f, CsvPointParser.GetFloat(row, 1, 7f), 4);
        Assert.Equal(7f, CsvPointParser.GetFloat(row, 9, 7f), 4);
        Assert.Equal(7f, CsvPointParser.GetFloat(row, -1, 7f), 4);
    }

    [Fact]
    public void GetColor_ScalesEightBitValues()
    {
        var row = new[] { "255", "128" };

        Assert.Equal(1f, CsvPointParser.GetColor(row, 0, true), 4);
        Assert.Equal(0.502f, CsvPointParser.GetColor(row, 1, true), 3);
        Assert.Equal(255f, CsvPointParser.GetColor(row, 0, false), 4);
        Assert.Equal(1f, CsvPointParser.GetColor(row, 5, true), 4);
    }

    [Fact]
    public void BuildOrientation_TreatsEulerAnglesAsDegrees()
    {
        var rotation = CsvPointParser.BuildOrientation(0, 0, 0, 90, false);
        var rotated = Vector3.Transform(Vector3.UnitX, rotation);

        Assert.Equal(0f, rotated.X, 4);
        Assert.Equal(1f, rotated.Y, 4);
        Assert.Equal(0f, rotated.Z, 4);
    }

    [Fact]
    public void BuildOrientation_NormalizesQuaternionAndFallsBackToIdentity()
    {
        Assert.Equal(Quaternion.Identity, CsvPointParser.BuildOrientation(0, 0, 0, 0, true));

        var scaled = CsvPointParser.BuildOrientation(0, 0, 0, 2, true);
        Assert.Equal(1f, scaled.Length(), 4);
    }
}
