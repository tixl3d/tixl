#nullable enable
using System;
using System.Collections.Generic;
using T3.Core.DataTypes;

namespace Lib.Utils;

/// <summary>
/// Attribute names offered by the type-ahead of geometry ops' name inputs, and the meaning of a selection input:
/// empty is Auto (the flowing Selection if the input has one, otherwise everything), <see cref="All"/> ignores any selection.
/// Both are shown in angle brackets, like the editor's &lt;null&gt;, so they can't be mistaken for attribute names.
/// A plain "All" stored before the brackets were added still means All.
/// </summary>
internal static class GeometryAttributeChoices
{
    public const string All = "<All>";
    private const string LegacyAll = "All";
    private const string Auto = "<Auto>";

    public static bool IsAll(string? selectionName)
    {
        var trimmed = selectionName?.Trim();
        return string.Equals(trimmed, All, StringComparison.OrdinalIgnoreCase) || string.Equals(trimmed, LegacyAll, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Shows what an empty selection input resolves to, so the default is visible and can be changed.</summary>
    public static string GetSelectionDisplayValue(string? value, Geometry? lastSource)
    {
        if (!string.IsNullOrWhiteSpace(value))
            return value;

        return lastSource != null && HasFloatAttribute(lastSource, GeometryAttributeNames.Selection)
                   ? "<Auto: Selection>"
                   : "<Auto: All>";
    }

    public static IEnumerable<string> GetSelectionOptions(Geometry? lastSource)
    {
        yield return Auto;
        yield return All;
        foreach (var name in GetAttributeNames(lastSource, null, floatsOnly: true))
            yield return name;
    }

    /// <summary>Maps a picked or typed entry back to the stored value: any Auto entry is stored as empty.</summary>
    public static string ToSelectionValue(string selected)
    {
        var trimmed = selected.Trim();
        return trimmed.StartsWith("<Auto", StringComparison.OrdinalIgnoreCase) ? string.Empty : trimmed;
    }

    /// <summary>Distinct attribute names of the geometry, optionally of one domain or only floats.</summary>
    public static IEnumerable<string> GetAttributeNames(Geometry? geometry, AttributeDomain? domain, bool floatsOnly)
    {
        if (geometry == null)
            yield break;

        var attributes = geometry.Attributes;
        for (var index = 0; index < attributes.Count; index++)
        {
            var attribute = attributes[index];
            if ((domain != null && attribute.Domain != domain) || (floatsOnly && attribute is not GeometryAttribute<float>))
                continue;

            if (!IsListedBefore(attributes, index, attribute.Name, domain, floatsOnly))
                yield return attribute.Name;
        }
    }

    private static bool IsListedBefore(GeometryAttributes attributes, int end, string name, AttributeDomain? domain, bool floatsOnly)
    {
        for (var index = 0; index < end; index++)
        {
            var attribute = attributes[index];
            if ((domain == null || attribute.Domain == domain)
                && (!floatsOnly || attribute is GeometryAttribute<float>)
                && string.Equals(attribute.Name, name, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool HasFloatAttribute(Geometry geometry, string name)
    {
        var attributes = geometry.Attributes;
        for (var index = 0; index < attributes.Count; index++)
        {
            if (attributes[index] is GeometryAttribute<float> && string.Equals(attributes[index].Name, name, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
