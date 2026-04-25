#nullable enable
using SlugSharp.Core;

namespace Lib.Utils.Slug;

/// <summary>
/// Convenience helpers around SlugSharp charset parsing/validation.
/// </summary>
public static class SlugCharsetUtils
{
    public static bool TryLoadCodePointsFromFile(string charsetFilePath,
                                                 bool validateFirst,
                                                 [NotNullWhen(true)] out HashSet<int>? codePoints,
                                                 [NotNullWhen(false)] out string? failureReason,
                                                 out CharsetValidationReport? validationReport)
    {
        codePoints = null;
        validationReport = null;

        if (string.IsNullOrWhiteSpace(charsetFilePath))
        {
            failureReason = "Charset file path is null or empty.";
            return false;
        }

        if (!File.Exists(charsetFilePath))
        {
            failureReason = $"Charset file does not exist: {charsetFilePath}";
            return false;
        }

        try
        {
            if (validateFirst)
            {
                validationReport = CharsetFileParser.ValidateFile(charsetFilePath);
                if (!validationReport.IsValid)
                {
                    failureReason = BuildValidationFailure(validationReport);
                    return false;
                }
            }

            codePoints = CharsetFileParser.LoadCodePoints(charsetFilePath);
            failureReason = null;
            return true;
        }
        catch (Exception e)
        {
            failureReason = $"Failed to parse charset file '{charsetFilePath}': {e.Message}";
            return false;
        }
    }

    public static bool TryParseCodePoints(string content,
                                          bool validateFirst,
                                          [NotNullWhen(true)] out HashSet<int>? codePoints,
                                          [NotNullWhen(false)] out string? failureReason,
                                          out CharsetValidationReport? validationReport)
    {
        codePoints = null;
        validationReport = null;

        if (content == null)
        {
            failureReason = "Charset content is null.";
            return false;
        }

        try
        {
            if (validateFirst)
            {
                validationReport = CharsetFileParser.ValidateContent(content);
                if (!validationReport.IsValid)
                {
                    failureReason = BuildValidationFailure(validationReport);
                    return false;
                }
            }

            codePoints = CharsetFileParser.ParseCodePoints(content);
            failureReason = null;
            return true;
        }
        catch (Exception e)
        {
            failureReason = $"Failed to parse charset content: {e.Message}";
            return false;
        }
    }

    public static string BuildValidationFailure(CharsetValidationReport report)
    {
        if (report.IsValid)
            return "Charset validation failed unexpectedly with no validation issues.";

        var issueSummary = report.Issues.Count > 0
                               ? string.Join(" | ", report.Issues.Select(i => $"line {i.LineNumber}: '{i.Token}' ({i.Message})"))
                               : "No issues were reported.";

        return $"Charset validation failed. Issues={report.Issues.Count}, duplicates={report.DuplicateCount}. {issueSummary}";
    }
}
