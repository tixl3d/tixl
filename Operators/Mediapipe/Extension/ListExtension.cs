namespace Mediapipe.Extension;

internal static class ListExtension
{
    public static void ResizeTo<T>(this List<T> list, int size)
    {
        if (list.Count > size) list.RemoveRange(size, list.Count - size);

        int count = size - list.Count;
        for (int i = 0; i < count; i++) list.Add(default!);
    }
}