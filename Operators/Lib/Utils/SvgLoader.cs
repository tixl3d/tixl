#nullable enable
using Svg;

namespace Lib.Utils;

public static class SvgLoader
{
    public static bool TryLoad(FileResource file, SvgDocument? currentValue, [NotNullWhen(true)] out SvgDocument? newValue, [NotNullWhen(false)] out string? failureReason)
    {
        try
        {
            newValue = SvgDocument.Open<SvgDocument>(file.AbsolutePath, (Dictionary<string, string>?)null);
            failureReason = null;
            return true;
        }
        catch (Exception e)
        {
            newValue = null;
            failureReason = $"Failed to load svg file:" + e.Message;
            return false;
        }
    }
}