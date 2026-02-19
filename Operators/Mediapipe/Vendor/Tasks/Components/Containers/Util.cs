namespace Mediapipe.Tasks.Components.Containers;

internal static class Util
{
    public static string Format<T>(T value)
    {
        return value == null ? "null" : $"{value}";
    }

    public static string Format(string? value)
    {
        return value == null ? "null" : $"\"{value}\"";
    }

    public static string Format<T>(List<T>? list)
    {
        if (list == null) return "null";
        string str = string.Join(", ", list.Select(x => x?.ToString() ?? "null"));
        return $"[{str}]";
    }
}