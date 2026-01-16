using T3.Core.DataTypes.Vector;

namespace T3.Editor.UiModel.InputsAndTypes;

public static class TypeUiRegistry
{
    private static readonly Dictionary<Type, UiProperties> _entries = new();

    public static UiProperties GetPropertiesForType(Type type)
    {
        return type != null && _entries.TryGetValue(type, out var properties) ? properties : UiProperties.Default;
    }

    internal static bool TryGetPropertiesForType(Type type, out UiProperties properties)
    {
        properties = UiProperties.Default;
        if (type == null)
            return false;

        if (_entries.TryGetValue(type, out properties))
        {
            return true;
        }
        properties = UiProperties.Default;
        return false;
    }
        
    internal static void SetProperties(Type type, UiProperties properties)
    {
        _entries[type] = properties;
    }

    internal static Color GetTypeOrDefaultColor(Type type)
    {
        if (type == null)
            return UiProperties.Default.Color;

        return _entries.TryGetValue(type, out var props) 
                   ? props.Color 
                   : UiProperties.Default.Color;
    }
}