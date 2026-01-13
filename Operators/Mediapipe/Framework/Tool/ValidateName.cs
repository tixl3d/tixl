using System.Text.RegularExpressions;

namespace Mediapipe.Framework.Tool;

/// <summary>
///     translated version of mediapipe/framework/tool/validate_name.cc
///     <summary />
internal static class Internal
{
    public const int MaxCollectionItemId = 10000;
}

public static partial class Tool
{
    private const string _NameRegex = "[a-z_][a-z0-9_]*";
    private const string _NumberRegex = "(0|[1-9][0-9]*)";
    private const string _TagRegex = "[A-Z_][A-Z0-9_]*";
    private static readonly string _TagAndNameRegex = $"({_TagRegex}:)?{_NameRegex}";
    private static readonly string _TagIndexNameRegex = $"({_TagRegex}:({_NumberRegex}:)?)?{_NameRegex}";
    private static readonly string _TagIndexRegex = $"({_TagRegex})?(:{_NumberRegex})?";

    private static readonly Regex _regex1 = new("^[a-z_][a-z0-9_]*$", RegexOptions.Compiled);
    internal static Regex MyRegex1() => _regex1;

    private static readonly Regex _regex2 = new("^(0|[1-9][0-9]*)$", RegexOptions.Compiled);
    internal static Regex MyRegex2() => _regex2;

    private static readonly Regex _regex3 = new("^[A-Z_][A-Z0-9_]*$", RegexOptions.Compiled);
    internal static Regex MyRegex3() => _regex3;

    public static void ValidateName(string name)
    {
        if (name.Length > 0 && MyRegex1().IsMatch(name)) return;
        throw new ArgumentException($"Name \"{name}\" does not match \"{_NameRegex}\".");
    }

    public static void ValidateNumber(string number)
    {
        if (number.Length > 0 && MyRegex2().IsMatch(number)) return;
        throw new ArgumentException($"Number \"{number}\" does not match \"{_NumberRegex}\".");
    }

    public static void ValidateTag(string tag)
    {
        if (tag.Length > 0 && MyRegex3().IsMatch(tag)) return;
        throw new ArgumentException($"Tag \"{tag}\" does not match \"{_TagRegex}\".");
    }

    public static void ParseTagAndName(string tagAndName, out string tag, out string name)
    {
        int nameIndex = -1;
        string[] v = tagAndName.Split(':');

        try
        {
            if (v.Length == 1)
            {
                ValidateName(v[0]);
                nameIndex = 0;
            }
            else if (v.Length == 2)
            {
                ValidateTag(v[0]);
                ValidateName(v[1]);
                nameIndex = 1;
            }

            if (nameIndex == -1) throw new ArgumentException("tagAndName is invalid", nameof(tagAndName));
        }
        catch (ArgumentException)
        {
            throw new ArgumentException(
                $"\"tag and name\" is invalid, \"{tagAndName}\" does not match \"{_TagAndNameRegex}\" (examples: \"TAG:name\", \"longer_name\").");
        }

        tag = nameIndex == 1 ? v[0] : "";
        name = v[nameIndex];
    }

    public static void ParseTagIndexName(string tagIndexName, out string tag, out int index, out string name)
    {
        int nameIndex = -1;
        int theIndex = 0;
        string[] v = tagIndexName.Split(':');

        try
        {
            if (v.Length == 1)
            {
                ValidateName(v[0]);
                theIndex = -1;
                nameIndex = 0;
            }
            else if (v.Length == 2)
            {
                ValidateTag(v[0]);
                ValidateName(v[1]);
                nameIndex = 1;
            }
            else if (v.Length == 3)
            {
                ValidateTag(v[0]);
                ValidateNumber(v[1]);

                theIndex = int.TryParse(v[1], out int result) && result <= Internal.MaxCollectionItemId
                    ? result
                    : throw new ArgumentException("tagIndexName is invalid", nameof(tagIndexName));
                ValidateName(v[2]);
                nameIndex = 2;
            }

            if (nameIndex == -1) throw new ArgumentException("tagIndexName is invalid", nameof(tagIndexName));
        }
        catch (ArgumentException)
        {
            throw new ArgumentException(
                $"TAG:index:name is invalid, \"{tagIndexName}\" does not match \"{_TagIndexNameRegex}\" (examples: \"TAG:name\", \"VIDEO:2:name_b\", \"longer_name\").");
        }

        tag = nameIndex != 0 ? v[0] : "";
        index = theIndex;
        name = v[nameIndex];
    }

    public static void ParseTagIndex(string tagIndex, out string tag, out int index)
    {
        int theIndex = -1;
        string[] v = tagIndex.Split(':');

        try
        {
            if (v.Length == 1)
            {
                if (v[0].Length != 0) ValidateTag(v[0]);
                theIndex = 0;
            }
            else if (v.Length == 2)
            {
                if (v[0].Length != 0) ValidateTag(v[0]);
                ValidateNumber(v[1]);

                theIndex = int.TryParse(v[1], out int result) && result <= Internal.MaxCollectionItemId
                    ? result
                    : throw new ArgumentException("tagIndex is invalid", nameof(tagIndex));
            }

            if (theIndex == -1) throw new ArgumentException("tagIndex is invalid", nameof(tagIndex));
        }
        catch (ArgumentException)
        {
            throw new ArgumentException(
                $"TAG:index is invalid, \"{tagIndex}\" does not match \"{_TagIndexRegex}\" (examples: \"TAG\", \"VIDEO:2\").");
        }

        tag = v[0];
        index = theIndex;
    }
}
