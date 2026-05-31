#nullable enable
using System.Globalization;
using System.IO;
using System.Text;
using T3.Editor.Gui.Help;

namespace T3.Editor.Gui.Windows.TestRunner;

/// <summary>
/// Reads <c>.tests-manual/*.md</c> files into <see cref="TestSet"/> values.
/// The format is documented in <c>.tests-manual/README.md</c> — a small
/// hand-rolled YAML-ish frontmatter parser plus a body splitter on
/// <c>## Step:</c> headers.
/// </summary>
internal static class TestSetParser
{
    public static string ResolveTestsDirectory()
    {
        // Dev: the repo's <repo>/.tests-manual/ (live files). Packaged release: the copy shipped next
        // to the binaries (see Editor.csproj). ShippedContent handles both.
        return ShippedContent.ResolveDirectory(".tests-manual");
    }

    public static List<TestSet> LoadAll(string directory)
    {
        var result = new List<TestSet>();
        if (!Directory.Exists(directory))
            return result;

        foreach (var path in Directory.EnumerateFiles(directory, "*.md", SearchOption.TopDirectoryOnly))
        {
            // Skip the README; not a test set.
            if (Path.GetFileName(path).Equals("README.md", StringComparison.OrdinalIgnoreCase))
                continue;

            var set = TryParseFile(path);
            if (set != null)
                result.Add(set);
        }

        Sort(result, TestSetSort.RecentlyAdded);
        return result;
    }

    /// <summary>Sorts in place by the chosen mode; ties break on title.</summary>
    public static void Sort(List<TestSet> sets, TestSetSort mode)
    {
        switch (mode)
        {
            case TestSetSort.RecentlyAdded:
                sets.Sort((a, b) =>
                          {
                              var byDate = b.Added.CompareTo(a.Added); // newest first
                              return byDate != 0 ? byDate : string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
                          });
                break;
            case TestSetSort.ByScope:
                sets.Sort((a, b) =>
                          {
                              var byScope = string.Compare(a.Scope, b.Scope, StringComparison.OrdinalIgnoreCase);
                              return byScope != 0 ? byScope : string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
                          });
                break;
            default:
                sets.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));
                break;
        }
    }

    public static TestSet? TryParseFile(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch
        {
            return null;
        }

        return Parse(text, path);
    }

    public static TestSet Parse(string text, string sourcePath)
    {
        var warnings = new List<string>();
        var lines = text.Replace("\r\n", "\n").Split('\n');

        // Find frontmatter delimited by leading and trailing `---`.
        var frontmatter = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var bodyStart = 0;
        if (lines.Length > 0 && lines[0].Trim() == "---")
        {
            var closeIdx = -1;
            for (var i = 1; i < lines.Length; i++)
            {
                if (lines[i].Trim() == "---")
                {
                    closeIdx = i;
                    break;
                }
            }

            if (closeIdx > 0)
            {
                ParseFrontmatter(lines, 1, closeIdx, frontmatter, warnings);
                bodyStart = closeIdx + 1;
            }
            else
            {
                warnings.Add("Frontmatter is missing its closing `---` line.");
            }
        }

        var id = AsString(frontmatter, "id") ?? Path.GetFileNameWithoutExtension(sourcePath);
        var title = AsString(frontmatter, "title") ?? id;
        var scope = AsString(frontmatter, "scope") ?? string.Empty;
        var tags = AsStringList(frontmatter, "tags");
        var prerequisites = AsStringList(frontmatter, "prerequisites");
        var relatedHelp = AsStringList(frontmatter, "related-help");

        var addedRaw = AsString(frontmatter, "added");
        var added = DateTime.TryParse(addedRaw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var addedDate)
                        ? addedDate
                        : DateTime.MinValue;
        var addedInVersion = AsString(frontmatter, "added-in-version") ?? AsString(frontmatter, "addedInVersion") ?? string.Empty;

        ParseBody(lines, bodyStart, out var intro, out var steps, warnings);

        if (steps.Count == 0)
            warnings.Add("No steps found. Each step must start with `## Step: <title>`.");

        return new TestSet
                   {
                       Id = id,
                       Title = title,
                       Scope = scope,
                       Tags = tags,
                       Added = added,
                       AddedInVersion = addedInVersion,
                       Prerequisites = prerequisites,
                       RelatedHelp = relatedHelp,
                       Intro = intro,
                       Steps = steps,
                       SourcePath = sourcePath,
                       ParseWarnings = warnings,
                   };
    }

    private static void ParseFrontmatter(string[] lines, int from, int toExclusive,
                                          Dictionary<string, object> result, List<string> warnings)
    {
        string? blockListKey = null;
        List<string>? blockList = null;

        for (var i = from; i < toExclusive; i++)
        {
            var raw = lines[i];
            if (string.IsNullOrWhiteSpace(raw))
            {
                FlushBlockList();
                continue;
            }

            // Continuation of a block list ("  - item" indented under a key).
            var trimmed = raw.TrimStart();
            if (blockListKey != null && trimmed.StartsWith("- "))
            {
                blockList!.Add(trimmed.Substring(2).Trim());
                continue;
            }

            FlushBlockList();

            var colonIdx = raw.IndexOf(':');
            if (colonIdx < 0)
            {
                warnings.Add($"Frontmatter line ignored (no `:`): `{raw}`");
                continue;
            }

            var key = raw.Substring(0, colonIdx).Trim();
            var value = raw.Substring(colonIdx + 1).Trim();

            if (value.Length == 0)
            {
                blockListKey = key;
                blockList = new List<string>();
                continue;
            }

            if (value.StartsWith('[') && value.EndsWith(']'))
            {
                var inner = value.Substring(1, value.Length - 2);
                var items = inner.Split(',');
                var list = new List<string>(items.Length);
                foreach (var item in items)
                {
                    var v = item.Trim().Trim('"', '\'');
                    if (v.Length > 0)
                        list.Add(v);
                }
                result[key] = list;
                continue;
            }

            result[key] = StripQuotes(value);
        }

        FlushBlockList();

        void FlushBlockList()
        {
            if (blockListKey == null || blockList == null)
                return;
            result[blockListKey] = blockList;
            blockListKey = null;
            blockList = null;
        }
    }

    private static void ParseBody(string[] lines, int bodyStart,
                                   out string intro,
                                   out List<TestStep> steps,
                                   List<string> warnings)
    {
        steps = new List<TestStep>();

        // Intro is everything between bodyStart and the first `## Step:` header.
        var firstStep = -1;
        for (var i = bodyStart; i < lines.Length; i++)
        {
            if (IsStepHeader(lines[i]))
            {
                firstStep = i;
                break;
            }
        }

        if (firstStep < 0)
        {
            intro = ConcatTrim(lines, bodyStart, lines.Length);
            return;
        }

        intro = ConcatTrim(lines, bodyStart, firstStep);

        // Walk steps. Each step runs until the next `## Step:` header or EOF.
        var i2 = firstStep;
        while (i2 < lines.Length)
        {
            var stepTitle = ExtractStepTitle(lines[i2]);
            var stepStart = i2 + 1;
            var stepEnd = lines.Length;
            for (var j = stepStart; j < lines.Length; j++)
            {
                if (IsStepHeader(lines[j]))
                {
                    stepEnd = j;
                    break;
                }
            }

            var step = ParseStep(lines, stepStart, stepEnd, stepTitle, warnings);
            steps.Add(step);
            i2 = stepEnd;
        }
    }

    private static TestStep ParseStep(string[] lines, int from, int toExclusive,
                                       string title, List<string> warnings)
    {
        string? context = null;
        var action = new StringBuilder();
        var expected = new StringBuilder();
        var collecting = StepSection.None;

        for (var i = from; i < toExclusive; i++)
        {
            var raw = lines[i];
            var trimmed = raw.TrimStart();

            if (trimmed.StartsWith("**Context:**"))
            {
                var rest = trimmed.Substring("**Context:**".Length).Trim();
                context = rest.Length > 0 ? rest : null;
                collecting = StepSection.Context;
                continue;
            }

            if (trimmed.StartsWith("**Action:**"))
            {
                collecting = StepSection.Action;
                var rest = trimmed.Substring("**Action:**".Length).Trim();
                if (rest.Length > 0)
                    action.Append(rest).Append('\n');
                continue;
            }

            if (trimmed.StartsWith("**Expected:**"))
            {
                collecting = StepSection.Expected;
                var rest = trimmed.Substring("**Expected:**".Length).Trim();
                if (rest.Length > 0)
                    expected.Append(rest).Append('\n');
                continue;
            }

            // Everything else is captured verbatim into the current section so
            // the markdown renderer can handle bullets, paragraphs, and inline
            // styling without the parser dictating shape.
            switch (collecting)
            {
                case StepSection.Action:
                    action.Append(raw).Append('\n');
                    break;
                case StepSection.Expected:
                    expected.Append(raw).Append('\n');
                    break;
                case StepSection.Context when trimmed.Length > 0:
                    context = string.IsNullOrEmpty(context) ? trimmed : context + " " + trimmed;
                    break;
            }
        }

        var actionMd = action.ToString().Trim();
        var expectedMd = expected.ToString().Trim();

        if (actionMd.Length == 0 && expectedMd.Length == 0)
            warnings.Add($"Step \"{title}\" has no Action or Expected content.");

        return new TestStep
                   {
                       Title = title,
                       Context = context,
                       ActionMarkdown = actionMd,
                       ExpectedMarkdown = expectedMd,
                   };
    }

    private static bool IsStepHeader(string line) =>
        line.StartsWith("## Step:", StringComparison.OrdinalIgnoreCase);

    private static string ExtractStepTitle(string line) =>
        line.Substring("## Step:".Length).Trim();

    private static string ConcatTrim(string[] lines, int from, int toExclusive)
    {
        if (from >= toExclusive)
            return string.Empty;
        var sb = new StringBuilder();
        for (var i = from; i < toExclusive; i++)
        {
            if (sb.Length > 0)
                sb.Append('\n');
            sb.Append(lines[i]);
        }
        return sb.ToString().Trim();
    }

    private static string? AsString(Dictionary<string, object> map, string key)
        => map.TryGetValue(key, out var v) && v is string s ? s : null;

    private static IReadOnlyList<string> AsStringList(Dictionary<string, object> map, string key)
    {
        if (!map.TryGetValue(key, out var v))
            return Array.Empty<string>();
        if (v is List<string> list)
            return list;
        if (v is string s)
            return new[] { s };
        return Array.Empty<string>();
    }

    private static string StripQuotes(string s)
    {
        if (s.Length >= 2 && (s[0] == '"' || s[0] == '\'') && s[s.Length - 1] == s[0])
            return s.Substring(1, s.Length - 2);
        return s;
    }

    private enum StepSection
    {
        None,
        Context,
        Action,
        Expected,
    }
}
