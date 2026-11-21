#nullable enable
using System.Globalization;
using System.Numerics;
using System.Text;

namespace Lib.Utils;

/// <summary>
/// Pure CSV parsing and numeric conversion rules used by <c>CsvToPoints</c>.
/// </summary>
/// <remarks>
/// Kept free of GPU resources and operator state so the file-format rules can be unit-tested
/// without an <c>EvaluationContext</c>. Parsing is culture-invariant: a file written with
/// <c>.</c> decimal separators reads the same on every machine.
///
/// Supported syntax:
/// <list type="bullet">
/// <item>comma separated fields, quoted fields (<c>"a,b"</c>) and escaped quotes (<c>""</c>);</item>
/// <item>quoted fields may contain line breaks;</item>
/// <item>LF and CRLF line endings; blank lines are skipped;</item>
/// <item>numeric cells may carry a unit suffix (<c>-2.25m</c>, <c>26.8°</c>, <c>300W</c>, <c>50%</c>).</item>
/// </list>
/// </remarks>
internal static class CsvPointParser
{
    /// <summary>
    /// Splits CSV text into rows of fields without a row limit.
    /// </summary>
    public static List<string[]> Parse(string text)
    {
        return Parse(text, int.MaxValue, out _);
    }

    /// <summary>
    /// Splits CSV text into rows of fields. Rows that contain only whitespace are dropped, so
    /// trailing newlines and decorative blank lines do not produce empty points.
    /// </summary>
    /// <param name="text">The complete file contents.</param>
    /// <param name="maxRows">Maximum number of rows to keep, including the header row.</param>
    /// <param name="exceededRowLimit">
    /// True when the text contains at least one more non-empty row than <paramref name="maxRows"/>.
    /// Parsing stops at that point, which bounds the memory a malformed or oversized file can claim.
    /// </param>
    public static List<string[]> Parse(string text, int maxRows, out bool exceededRowLimit)
    {
        exceededRowLimit = false;
        var rows = new List<string[]>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < text.Length && !exceededRowLimit; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    break;
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    exceededRowLimit = !AddRow(rows, fields, field, maxRows);
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        if (!exceededRowLimit && (field.Length > 0 || fields.Count > 0))
        {
            AddRow(rows, fields, field, maxRows);
        }

        return rows;
    }

    /// <summary>
    /// Trims a header cell and removes a UTF-8 byte-order mark, if the file starts with one.
    /// </summary>
    public static string NormalizeHeader(string raw)
    {
        return raw.AsSpan().TrimStart('\uFEFF').Trim().ToString();
    }

    /// <summary>
    /// Parses a numeric cell. Returns <c>false</c> for empty, non-numeric, NaN and infinite values,
    /// which callers replace with their attribute default.
    /// </summary>
    public static bool TryParseNumber(string raw, out float value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var text = raw.AsSpan().Trim();
        if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return float.IsFinite(value);
        }

        // Fixture exports append units ("-2.25m", "26.8°", "300W"); retry without the unit suffix only.
        var end = text.Length;
        while (end > 0 && (char.IsLetter(text[end - 1]) || text[end - 1] is '°' or '%'))
        {
            end--;
        }

        return end > 0
               && end < text.Length
               && float.TryParse(text[..end], NumberStyles.Float, CultureInfo.InvariantCulture, out value)
               && float.IsFinite(value);
    }

    /// <summary>
    /// Reads a per-point float attribute, falling back when the column is unmapped, the row is
    /// shorter than the header, or the cell is not a number.
    /// </summary>
    public static float GetFloat(string[] row, int index, float fallback)
    {
        if (index < 0 || index >= row.Length || !TryParseNumber(row[index], out var value))
        {
            return fallback;
        }

        return value;
    }

    /// <summary>
    /// Reads a color channel, optionally scaling 8-bit values (0..255) down to 0..1.
    /// </summary>
    public static float GetColor(string[] row, int index, bool colorIs0To255)
    {
        if (index < 0 || index >= row.Length || !TryParseNumber(row[index], out var value))
        {
            return 1;
        }

        return colorIs0To255 ? value / 255f : value;
    }

    /// <summary>
    /// Builds a rotation from either Euler angles in degrees or raw quaternion components.
    /// Degenerate quaternions fall back to <see cref="Quaternion.Identity"/> instead of NaN.
    /// </summary>
    public static Quaternion BuildOrientation(float w, float x, float y, float z, bool isQuaternion)
    {
        if (isQuaternion)
        {
            var quaternion = new Quaternion(x, y, z, w);
            return quaternion.LengthSquared() > 0.000001f ? Quaternion.Normalize(quaternion) : Quaternion.Identity;
        }

        var qx = Quaternion.CreateFromAxisAngle(Vector3.UnitX, x * (MathF.PI / 180f));
        var qy = Quaternion.CreateFromAxisAngle(Vector3.UnitY, y * (MathF.PI / 180f));
        var qz = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, z * (MathF.PI / 180f));

        // Euler order XYZ rotates around Z first, then Y, then X.
        return Quaternion.Concatenate(Quaternion.Concatenate(qz, qy), qx);
    }

    /// <summary>
    /// Appends a finished row. Returns <c>false</c> when the row budget is exhausted, which makes
    /// the caller abandon the rest of the file.
    /// </summary>
    private static bool AddRow(List<string[]> rows, List<string> fields, StringBuilder field, int maxRows)
    {
        fields.Add(field.ToString());
        field.Clear();

        var isEmpty = true;
        for (var i = 0; i < fields.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(fields[i]))
            {
                isEmpty = false;
                break;
            }
        }

        if (!isEmpty)
        {
            if (rows.Count >= maxRows)
            {
                fields.Clear();
                return false;
            }

            rows.Add(fields.ToArray());
        }

        fields.Clear();
        return true;
    }
}
