#nullable enable
namespace T3.Editor.Gui.UiHelpers.Enums;

/// <summary>How an enum value is offered in a dropdown.</summary>
internal enum OptionRelevance
{
    Normal,

    /** Listed first under a group title, above a separator. */
    Important,

    /** Shown disabled because picking it would fail on this machine. */
    Unavailable,
}

/// <param name="Label">A readable name for an important value. The value name then moves into the tooltip.</param>
internal readonly record struct EnumOptionHint(OptionRelevance Relevance, string? Label = null, string? Tooltip = null);

/// <summary>
/// Describes the values of one enum for dropdowns: which matter most, readable labels and availability.
/// Projects store enum values by name, so nothing here may affect what is saved.
/// </summary>
internal interface IEnumOptionHints
{
    Type EnumType { get; }

    /** Title of the group of important values at the top of the dropdown. */
    string ImportantGroupTitle { get; }

    EnumOptionHint GetHint(string valueName, object value);
}

internal static class EnumOptionHintRegistry
{
    public static IEnumOptionHints? Find(Type enumType) => _providers.GetValueOrDefault(enumType);

    private static Dictionary<Type, IEnumOptionHints> Create(params IEnumOptionHints[] providers)
    {
        var result = new Dictionary<Type, IEnumOptionHints>();
        foreach (var provider in providers)
        {
            result[provider.EnumType] = provider;
        }

        return result;
    }

    private static readonly Dictionary<Type, IEnumOptionHints> _providers = Create(new FormatOptionHints());
}
