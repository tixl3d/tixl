using System.Text;

namespace ProjectUpdater;

internal static class Utils
{
    public static void ConvertLineEndingsOf(ref string text, bool useWindows)
    {
        if (useWindows)
        {
            text = text
                  .Replace("\r\n", "\n")
                  .Replace("\n", "\r\n");
        }
        else
        {
            text = text.Replace("\r\n", "\n");
        }
    }

    public static string[] GetSubfolderArray(string filePath, string parentDirectory, StringBuilder sb)
    {
        var fileDirectory = Path.GetDirectoryName(filePath)!;
        var dirWithoutRoot = fileDirectory[(parentDirectory.Length + 1)..];

        for (var index = 0; index < dirWithoutRoot.Length; index++)
        {
            char c = dirWithoutRoot[index];
            if (c == Path.AltDirectorySeparatorChar)
                c = Path.DirectorySeparatorChar;

            sb.Append(c);
        }

        var subfolderComponents = sb.ToString().Split(Path.DirectorySeparatorChar);
        return subfolderComponents;
    }
}