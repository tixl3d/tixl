#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

// ReSharper disable MemberCanBePrivate.Global

namespace T3.Core.Utils;

[SuppressMessage("ReSharper", "MemberCanBeInternal")]
public static class StringUtils
{
    public static unsafe bool Equals(ReadOnlySpan<char> a, ReadOnlySpan<char> b, bool ignoreCase)
    {
        var aLength = a.Length;
        if (aLength != b.Length)
            return false;

        // this is made unsafe to avoid the overhead of the span bounds check - it's safe because we checked the length already
        if (ignoreCase)
        {
            fixed (char* aPtr = a)
            fixed (char* bPtr = b)
            {
                for (var i = 0; i < aLength; i++)
                {
                    if (char.ToLowerInvariant(aPtr[i]) != char.ToLowerInvariant(bPtr[i]))
                        return false;
                }
            }
        }
        else
        {
            fixed (char* aPtr = a)
            fixed (char* bPtr = b)
            {
                for (var i = 0; i < aLength; i++)
                {
                    if (aPtr[i] != bPtr[i])
                        return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Splits the provided path by the directory separator character.
    /// Warning: not for use with windows-style absolute paths (e.g. C:\foo\bar or C:/foo/bar),
    /// though unix-style absolute paths will work (e.g. /foo/bar)
    /// </summary>
    /// <param name="path">The path to split</param>
    /// <param name="ranges">Ranges that can be used to create spans or substrings from the original path</param>
    /// <returns></returns>
    public static int SplitByDirectory(ReadOnlySpan<char> path, Span<Range> ranges)
    {
        int count = 0;
        int start = 0;
        for (int i = 0; i < path.Length; i++)
        {
            if (path[i] == '\\' || path[i] == '/')
            {
                if (i > start)
                {
                    ranges[count++] = new Range(start, i);
                }

                start = i + 1;
            }
        }

        if (path.Length > start)
        {
            ranges[count++] = new Range(start, path.Length);
        }

        return count;
    }

    public enum SearchResultIndex
    {
        BeforeTerm,
        AfterTerm,
        FirstIndex,
        LastIndex
    }

    public static bool TryFindIgnoringAllWhitespace(string text, string searchTerm, SearchResultIndex searchResultIndex, out int indexFollowingSearchTerm,
                                                    int startIndex = 0)
    {
        // search the given string for the search term, ignoring all whitespace in both strings. " \ta b" == "ab"
        var searchTextLength = text.Length;

        // remove all whitespace from searchTerm
        searchTerm = RemoveWhitespaceFrom(searchTerm);

        var searchTermLength = searchTerm.Length;

        int currentSearchIndex = 0;
        char currentSearchChar = searchTerm[currentSearchIndex];
        int firstIndex = -1;

        for (int j = startIndex; j < searchTextLength; j++)
        {
            var textChar = text[j];

            if (char.IsWhiteSpace(textChar))
                continue;

            if (text[j] != currentSearchChar)
            {
                currentSearchIndex = 0;
                currentSearchChar = searchTerm[0];
                firstIndex = -1;
                continue;
            }

            if (firstIndex == -1)
                firstIndex = j;

            ++currentSearchIndex;
            if (currentSearchIndex == searchTermLength)
            {
                indexFollowingSearchTerm = searchResultIndex switch
                                               {
                                                   SearchResultIndex.BeforeTerm => firstIndex - 1,
                                                   SearchResultIndex.AfterTerm  => j + 1,
                                                   SearchResultIndex.FirstIndex => firstIndex,
                                                   SearchResultIndex.LastIndex  => j,
                                                   _                            => throw new ArgumentOutOfRangeException(nameof(searchResultIndex))
                                               };
                indexFollowingSearchTerm = j + 1;
                return true;
            }

            currentSearchChar = searchTerm[currentSearchIndex];
        }

        indexFollowingSearchTerm = -1;
        return false;
    }

    public static string RemoveWhitespaceFrom(string str)
    {
        var strLength = str.Length;
        for (var i = 0; i < strLength; i++)
        {
            var c = str[i];
            if (char.IsWhiteSpace(c))
            {
                str = str.Remove(c);
                strLength = str.Length;
                i = 0;
            }
        }

        return str;
    }

    public static ReadOnlySpan<char> TrimStringToLineCount(ReadOnlySpan<char> message, int maxLines)
    {
        var messageLength = message.Length;

        if (messageLength == 0)
            return message;

        int lineCount = 0;
        int length = 0;
        int nextStartIndex = 0;

        while (lineCount < maxLines)
        {
            lineCount++;

            var newlineIndex = message[nextStartIndex..].IndexOf('\n');
            nextStartIndex += newlineIndex + 1;

            if (newlineIndex == -1 || nextStartIndex == messageLength)
            {
                length = messageLength;
                break;
            }

            length = nextStartIndex;
        }

        return message[..length];
    }

    public static int IndexOfNot(this ReadOnlySpan<char> span, char c, bool ignoreCase, out char nextChar)
    {
        if (ignoreCase)
        {
            c = char.ToLowerInvariant(c);
            for (var i = 0; i < span.Length; i++)
            {
                nextChar = span[i];
                if (char.ToLowerInvariant(nextChar) != c)
                {
                    return i;
                }
            }

            nextChar = default;
            return -1;
        }

        for (var i = 0; i < span.Length; i++)
        {
            nextChar = span[i];
            if (nextChar != c)
                return i;
        }

        nextChar = default;
        return -1;
    }

    public static int LineCount(this string input)
    {
        if (string.IsNullOrEmpty(input))
            return 0;

        int count = 1; // At least one line if the string is not empty
        for (int i = 0; i < input.Length; i++)
        {
            if (input[i] == '\n')
                count++;
        }

        return count;
    }

    public static int IndexOf(this ReadOnlySpan<char> span, char c, bool ignoreCase)
    {
        if (ignoreCase)
        {
            c = char.ToLowerInvariant(c);
            for (var i = 0; i < span.Length; i++)
            {
                if (char.ToLowerInvariant(span[i]) == c)
                    return i;
            }

            return -1;
        }

        for (var i = 0; i < span.Length; i++)
        {
            if (span[i] == c)
                return i;
        }

        return -1;
    }

    public static int LastIndexOf(this ReadOnlySpan<char> span, char c, bool ignoreCase)
    {
        if (ignoreCase)
        {
            c = char.ToLowerInvariant(c);
            for (var i = span.Length - 1; i >= 0; i--)
            {
                if (char.ToLowerInvariant(span[i]) == c)
                    return i;
            }

            return -1;
        }

        for (var i = span.Length - 1; i >= 0; i--)
        {
            if (span[i] == c)
                return i;
        }

        return -1;
    }

    public static int LastIndexOfNot(this ReadOnlySpan<char> span, char c, bool ignoreCase, out char precedingChar)
    {
        if (ignoreCase)
        {
            c = char.ToLowerInvariant(c);
            for (var i = span.Length - 1; i >= 0; i--)
            {
                precedingChar = span[i];
                ;
                if (char.ToLowerInvariant(precedingChar) != c)
                    return i;
            }

            precedingChar = default;
            return -1;
        }

        for (var i = span.Length - 1; i >= 0; i--)
        {
            precedingChar = span[i];
            if (precedingChar != c)
                return i;
        }

        precedingChar = default;
        return -1;
    }

    /// <summary>
    /// A naive implementation of a filtering algorithm that supports wildcards ('*').
    /// 
    /// It is designed to be highly optimized, but it may not behave how you expect.
    /// This will accept an infinite amount of wildcards and treat them all the same -
    /// so "a*b**c" will match "a/b/anything/c" (expected) and "a/anything/b/c" (possibly unexpected) -
    /// there is no special directory treatment as is standard in most file search implementations.
    /// 
    /// Technically, the search begins from the end of the filter and the end of the possible match, and works backwards. This is because
    /// 
    /// This is mostly intended for use in file path searches, where the end of the path is the most likely to be the most specific, and the end of the search term
    /// is most likely to change with consecutive calls.
    /// </summary>
    /// <param name="possibleMatch">string you want to check for a match</param>
    /// <param name="filter">The filter to match against</param>
    /// <param name="ignoreCase"></param>
    /// <returns>True if the provided string matches the provided filter</returns>
    public static bool MatchesSearchFilter(ReadOnlySpan<char> possibleMatch, ReadOnlySpan<char> filter, bool ignoreCase)
    {
        while (filter.Length > 0)
        {
            // the possible match has been exhausted but the filter has not
            if (possibleMatch.Length == 0)
                return false;

            var nextFilterChar = filter[^1];

            if (nextFilterChar == '*')
            {
                var nextNonWildcardIndex = filter.LastIndexOfNot('*', ignoreCase, out nextFilterChar);
                if (nextNonWildcardIndex == -1)
                    return true;

                var matchIndex = possibleMatch.LastIndexOf(nextFilterChar, ignoreCase);
                if (matchIndex == -1)
                    return false;

                // remove the last character from both strings to continue the search
                filter = filter[..nextNonWildcardIndex];
                possibleMatch = possibleMatch[..matchIndex];
            }
            else if (possibleMatch[^1] == nextFilterChar)
            {
                // remove the last character from both strings to continue the search
                possibleMatch = possibleMatch[..^1];
                filter = filter[..^1];
            }
            else
            {
                // no match /:
                return false;
            }
        }

        // we finished the filter - it's a match!
        return true;
    }

    public static unsafe void ReplaceCharUnsafe(this string str, char toReplace, char replacement)
    {
        fixed (char* strPtr = str)
        {
            for (int i = 0; i < str.Length; i++)
            {
                if (strPtr[i] == toReplace)
                    strPtr[i] = replacement;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToForwardSlashesUnsafe(this string str)
    {
        str.ReplaceCharUnsafe('\\', '/');
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string ToForwardSlashes(this string str) => str.Replace('\\', '/');

    public static string Truncate(this string? input, int maxLength = 10)
    {
        if (input == null)
            return "null";

        if (input.Length < maxLength)
        {
            return input;
        }

        return input[..Math.Min(input.Length, maxLength)] + "...";
    }

    public static string HumanReadableDurationFromSeconds(double seconds)
    {
        return $"{(int)(seconds / 60 / 60):00}:{(seconds / 60) % 60:00}:{seconds % 60:00}";
    }

    public static string GetReadableRelativeTime(this DateTime? time)
    {
        if (time == null)
            return "Unknown time";

        var timeSpan = DateTime.Now - time;
        return GetReadableRelativeTime(timeSpan);
    }

    public static string GetReadableRelativeTime(double time)
    {
        return GetReadableRelativeTime(TimeSpan.FromSeconds(time));
    }

    private static string GetReadableRelativeTime([DisallowNull] TimeSpan? timeSpan)
    {
        var seconds = timeSpan.Value.TotalSeconds;
        if (seconds < 60)
        {
            return "seconds ago";
        }

        var minutes = timeSpan.Value.TotalMinutes;
        if (minutes < 120)
        {
            return $"{minutes:0} min ago";
        }

        var hours = timeSpan.Value.TotalHours;
        if (hours < 30)
        {
            return $"{hours:0.0} h ago";
        }

        var days = timeSpan.Value.TotalDays;
        return $"{days:0.0} days ago";
    }

    private static readonly Regex _matchFileVersionPattern = new(@"\b(\d{1,4})\b");
    
    public static string AppendOrIncrementVersionNumber(this string original)
    {
        if (string.IsNullOrEmpty(original))
            return original;
        
        var result = _matchFileVersionPattern.Match(original);
        if (result.Success)
        {
            var versionString = result.Groups[1].Value;
            if (!int.TryParse(versionString, out var versionNumber)) 
                return original;

            var newVersionString = (versionNumber + 1).ToString();
            return original.Replace(versionString, newVersionString);            
        }

        return original + " 2";
    }

    /// <summary>
    /// Parse a string like...
    ///
    /// Failed to update shader "PixelShader_PixelShaderFromSource (5e6d5cb9-df8f-494d-b50e-31499b257f34)" in package "Types":
    /// Failed to compile shader 'PixelShader_PixelShaderFromSource (5e6d5cb9-df8f-494d-b50e-31499b257f34)'.
    /// Line 37,31:  error X3000: unrecognized identifier 'x'
    /// </summary>
    public static List<string> ParseShaderCompilationError(string log)
    {
        List<string> results = new();
        ReadOnlySpan<char> logSpan = log;

        ReadOnlySpan<char> separator = ": ";

        while (!logSpan.IsEmpty)
        {
            var newlineIndex = logSpan.IndexOf('\n');
            var line = newlineIndex >= 0 ? logSpan[..newlineIndex] : logSpan;

            line = line.TrimStart();

            if (line.StartsWith("Line "))
            {
                var commaIndex = line.IndexOf(',');
                if (commaIndex > 5 && int.TryParse(line.Slice(5, commaIndex - 5), out var lineNumber))
                {
                    // Find the error message after ": "
                    var firstColonIndex = line.IndexOf(": ");
                    if (firstColonIndex >= 0)
                    {
                        var t = firstColonIndex + separator.Length;
                        var secondColonIndex = line[t..].IndexOf(separator) + t;
                        if (secondColonIndex >= 0 && secondColonIndex + 2 < line.Length)
                        {
                            var errorMessage = line.Slice(secondColonIndex + 2);

                            // Skip the error code (first word)
                            var spaceIndex = errorMessage.IndexOf(' ');
                            if (spaceIndex >= 0 && spaceIndex + 1 < errorMessage.Length)
                            {
                                var errorString = new string(errorMessage.Slice(spaceIndex + 1)); // Minimal allocation
                                results.Add($"Line {lineNumber}: {errorString}");
                            }
                        }
                    }
                }
            }

            if (newlineIndex < 0) break;
            logSpan = logSpan[(newlineIndex + 1)..]; // Move to next line
        }

        return results;
    }

    public static string ShortenGuid(this Guid guid, int length = 7)
    {
        if (length < 1 || length > 22)
            throw new ArgumentOutOfRangeException(nameof(length), "Length must be between 1 and 22.");

        var guidBytes = guid.ToByteArray();
        var base64 = Convert.ToBase64String(guidBytes);
        var alphanumeric = base64.Replace("+", "").Replace("/", "").Replace("=", "");
        return alphanumeric[..length];
    }
    
    
    public static string ToValidClassName(this string input, string fallback = "ClassName")
    {
        if (string.IsNullOrWhiteSpace(input))
            return fallback;
        
        var cleaned = Regex.Replace(input, @"[^a-zA-Z0-9_]", "");
        
        if (char.IsDigit(cleaned, 0))
            cleaned = "_" + cleaned;
        
        return string.IsNullOrEmpty(cleaned) ? "ClassName" : cleaned;
    }
}