#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using T3.Core.Operator;
using T3.Core.Utils;
using T3.Editor.Gui.Graph.Dialogs;
using T3.Editor.Gui.MagGraph.Model;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Modification;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Skills.Ui;

/// <summary>
/// Syncs SkillQuest levels between the content repository (a checkout of github.com/tixl3d/skillquest)
/// and the Skills package. There is no record of which side changed last, so neither direction
/// silently overwrites edited content:
///
/// Import:
/// - Builds category/topic sections with all level ops inside the EditSkillQuest symbol.
/// - Matches markdown level headings to symbols via the shortened symbol id (<c># Title &amp;shortId</c>).
/// - Headings without an id first adopt an existing, unclaimed level of their topic with the same
///   title; only if there is none, a stub level symbol is duplicated from the template. Either way
///   the id is written back into the markdown, so re-running is idempotent.
/// - Applies the markdown tour points only to levels without tours of their own (or fresh stubs).
///   Levels whose existing tour differs from the markdown are kept and reported.
///
/// Export:
/// - Rewrites the markdown section of every listed level that has a tour, including tour point
///   ids and targets, and leaves the rest of the file untouched.
/// </summary>
internal static class SkillQuestStructureSync
{
    internal static void ImportFromRepository()
    {
        if (!TryGetSyncContext(out var repositoryPath, out var skillsProject, out var editUi))
            return;

        Log.Debug($"Importing SkillQuest structure from {repositoryPath}...");

        var topics = CollectTopics(repositoryPath, skillsProject);
        var adoptedCount = AdoptExistingLevels(topics, skillsProject);
        var stubCount = CreateMissingStubs(topics, skillsProject, editUi);
        WriteBackNewIds(topics);
        var tourCount = ApplyTourData(topics);
        BuildSections(editUi, topics);

        editUi.FlagAsModified();

        // The batch mutations above bypass per-action commands (and symbol creation can't be
        // undone anyway), so a partial undo would leave the graph inconsistent.
        UndoRedoStack.Clear();

        var levelCount = 0;
        foreach (var topic in topics)
        {
            levelCount += topic.Levels.Count;
        }

        Log.Debug($"SkillQuest import: {topics.Count} topics, {levelCount} levels, {adoptedCount} adopted, {stubCount} stubs created, {tourCount} tours applied.");
    }

    internal static void ExportToRepository()
    {
        if (!TryGetSyncContext(out var repositoryPath, out var skillsProject, out _))
            return;

        Log.Debug($"Exporting SkillQuest tours to {repositoryPath}...");

        var topics = CollectTopics(repositoryPath, skillsProject);
        AdoptExistingLevels(topics, skillsProject);

        var exportedCount = 0;
        var changedFileCount = 0;
        var sb = new StringBuilder();
        foreach (var topic in topics)
        {
            if (topic.MdPath == null)
                continue;

            var sectionsByHeadingIndex = new Dictionary<int, string>();
            foreach (var level in topic.Levels)
            {
                if (level.Tour == null || level.Symbol == null || !level.Symbol.TryGetSymbolUi(out var levelUi))
                    continue;

                // An empty tour would erase the markdown's content - same rule as the import guard.
                if (levelUi.TourPoints.Count == 0)
                    continue;

                sb.Clear();
                TourDataMarkdownExport.WriteSymbolTourMarkdown(levelUi, sb);
                sectionsByHeadingIndex[level.HeadingIndex] = sb.ToString();
                exportedCount++;
            }

            if (sectionsByHeadingIndex.Count == 0)
                continue;

            try
            {
                if (ReplaceLevelSections(topic.MdPath, sectionsByHeadingIndex))
                    changedFileCount++;
            }
            catch (Exception e)
            {
                Log.Warning($"Failed to export tours to {topic.MdPath}: {e.Message}");
            }
        }

        Log.Debug($"SkillQuest export: {exportedCount} levels written, {changedFileCount} files changed. Review with 'git diff' in {repositoryPath}.");
    }

    private static bool TryGetSyncContext(out string repositoryPath,
                                          [NotNullWhen(true)] out EditableSymbolProject? skillsProject,
                                          [NotNullWhen(true)] out SymbolUi? editUi)
    {
        skillsProject = null;
        editUi = null;

        if (!TryFindRepositoryFolder(out repositoryPath))
        {
            Log.Warning("Can't find skillquest content repository. Set SkillQuestRepositoryPath in user settings " +
                        "or place a 'skillquest' checkout next to the TiXL folder.");
            return false;
        }

        foreach (var project in EditableSymbolProject.AllProjects)
        {
            if (project.Name == "Skills")
            {
                skillsProject = project;
                break;
            }
        }

        if (skillsProject == null)
        {
            Log.Warning("Skills project is not loaded as editable project.");
            return false;
        }

        if (!SymbolUiRegistry.TryGetSymbolUi(EditSymbolId, out editUi))
        {
            Log.Warning("Can't find EditSkillQuest symbol in Skills package.");
            return false;
        }

        return true;
    }

    #region repository scanning
    private static bool TryFindRepositoryFolder(out string path)
    {
        path = UserSettings.Config.SkillQuestRepositoryPath;
        if (!string.IsNullOrWhiteSpace(path))
            return Directory.Exists(path);

        // Auto-discover a "skillquest" folder next to (or above) the running editor's folder tree
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "skillquest");
            if (Directory.Exists(candidate))
            {
                path = candidate;
                return true;
            }

            directory = directory.Parent;
        }

        return false;
    }

    private static List<Topic> CollectTopics(string repositoryPath, EditableSymbolProject skillsProject)
    {
        var topicsByCode = new Dictionary<string, Topic>(StringComparer.OrdinalIgnoreCase);
        var symbolsByShortId = new Dictionary<string, Symbol>();

        // Pass 1: existing symbols define topics (namespace) and the id lookup
        foreach (var symbol in skillsProject.Symbols.Values)
        {
            symbolsByShortId[symbol.Id.ShortenGuid()] = symbol;

            if (!TryGetTopicCodeFromNamespace(symbol.Namespace, out var code, out var categoryKey))
                continue;

            if (!topicsByCode.TryGetValue(code, out var topic))
            {
                topic = new Topic { Code = code, CategoryKey = categoryKey, Namespace = symbol.Namespace };
                topicsByCode[code] = topic;
            }
        }

        // Pass 2: markdown files define level order, titles, and tour content
        var mdFileBySymbolId = new Dictionary<Guid, string>();
        foreach (var categoryDir in Directory.GetDirectories(repositoryPath).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var folderName = Path.GetFileName(categoryDir);
            if (folderName.StartsWith('.') || folderName.StartsWith('_'))
                continue;

            foreach (var mdPath in Directory.GetFiles(categoryDir, "*.md").OrderBy(f => CleanFileName(f), StringComparer.OrdinalIgnoreCase))
            {
                var cleanName = CleanFileName(mdPath);
                if (!TrySplitTopicFileName(cleanName, out var code, out var slug))
                    continue;

                if (!topicsByCode.TryGetValue(code, out var topic))
                {
                    topic = new Topic { Code = code, CategoryKey = folderName };
                    topicsByCode[code] = topic;
                }

                topic.MdPath = mdPath;
                topic.MdFolderName = folderName;
                topic.NamespaceSlug = slug;

                var tours = new List<TourDataMarkdownExport.TourWithId>(TourDataMarkdownExport.GetToursFromMarkdown(File.ReadAllText(mdPath)));
                for (var headingIndex = 0; headingIndex < tours.Count; headingIndex++)
                {
                    var tour = tours[headingIndex];
                    var level = new Level { Tour = tour, Title = tour.Title, HeadingIndex = headingIndex };
                    if (!string.IsNullOrEmpty(tour.IdString))
                    {
                        if (!symbolsByShortId.TryGetValue(tour.IdString, out var symbol))
                        {
                            Log.Warning($"Level '{tour.Title}' in {cleanName} references unknown id &{tour.IdString} - skipping.");
                            continue;
                        }

                        // A level listed in two files would end up in whichever topic is laid out last,
                        // and its tour would be overwritten by the other file's (possibly stale) copy.
                        if (mdFileBySymbolId.TryGetValue(symbol.Id, out var claimingFile))
                        {
                            Log.Warning($"Level '{tour.Title}' &{tour.IdString} in {cleanName} is already listed in {claimingFile} - skipping.");
                            continue;
                        }

                        mdFileBySymbolId[symbol.Id] = cleanName;
                        level.Symbol = symbol;
                    }

                    topic.Levels.Add(level);
                }
            }
        }

        // Pass 3: append package symbols the markdown doesn't mention, so the overview stays complete
        foreach (var topic in topicsByCode.Values)
        {
            if (topic.Namespace == null)
                continue;

            var claimed = new HashSet<Guid>();
            foreach (var level in topic.Levels)
            {
                if (level.Symbol != null)
                    claimed.Add(level.Symbol.Id);
            }

            foreach (var symbol in skillsProject.Symbols.Values.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (symbol.Namespace != topic.Namespace || claimed.Contains(symbol.Id))
                    continue;

                topic.Levels.Add(new Level { Symbol = symbol, Title = symbol.Name });
            }
        }

        // Pass 4: derive namespaces for topics that only exist as markdown yet
        foreach (var topic in topicsByCode.Values)
        {
            if (topic.Namespace != null)
                continue;

            var categorySegment = FindCategorySegmentForFolder(topicsByCode.Values, topic.MdFolderName)
                                  ?? (topic.MdFolderName ?? topic.CategoryKey).ToValidClassName();

            topic.CategoryKey = categorySegment;
            topic.Namespace = $"Skills.{categorySegment}.{topic.Code}_{PascalCaseFromSlug(topic.NamespaceSlug)}";
        }

        var result = new List<Topic>(topicsByCode.Values);
        result.Sort((a, b) => string.Compare(a.Code, b.Code, StringComparison.OrdinalIgnoreCase));
        return result;
    }

    /// <summary>Topics already mapped to a namespace tell us the category segment their md folder maps to
    /// (e.g. folder "Rendering" → segment "Render").</summary>
    private static string? FindCategorySegmentForFolder(IEnumerable<Topic> topics, string? folderName)
    {
        if (folderName == null)
            return null;

        foreach (var other in topics)
        {
            if (other.Namespace == null || other.MdFolderName != folderName)
                continue;

            var parts = other.Namespace.Split('.');
            if (parts.Length >= 3)
                return parts[1];
        }

        return null;
    }

    private static bool TryGetTopicCodeFromNamespace(string symbolNamespace, out string code, out string categoryKey)
    {
        code = string.Empty;
        categoryKey = string.Empty;

        var parts = symbolNamespace.Split('.');
        if (parts.Length < 3)
            return false;

        foreach (var part in parts)
        {
            if (part.StartsWith('_'))
                return false;
        }

        var lastSegment = parts[^1];
        var underscoreIndex = lastSegment.IndexOf('_');
        if (underscoreIndex <= 0)
            return false;

        code = lastSegment[..underscoreIndex];
        if (!IsValidTopicCode(code))
            return false;

        categoryKey = parts[1];
        return true;
    }

    /// <summary>Strips completion-marker emojis etc. from file names like "✅R01-moving-things.md".</summary>
    private static string CleanFileName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var start = 0;
        while (start < name.Length && !char.IsAsciiLetterOrDigit(name[start]))
        {
            start++;
        }

        return name[start..];
    }

    private static bool TrySplitTopicFileName(string cleanName, out string code, out string slug)
    {
        code = string.Empty;
        slug = string.Empty;

        var dashIndex = cleanName.IndexOf('-');
        if (dashIndex <= 0 || dashIndex == cleanName.Length - 1)
            return false;

        code = cleanName[..dashIndex];
        slug = cleanName[(dashIndex + 1)..];
        return IsValidTopicCode(code);
    }

    /// <summary>Topic codes are letters followed by digits: R01, Me01, I20...</summary>
    private static bool IsValidTopicCode(string code)
    {
        var digitIndex = 0;
        while (digitIndex < code.Length && char.IsAsciiLetter(code[digitIndex]))
        {
            digitIndex++;
        }

        if (digitIndex == 0 || digitIndex == code.Length)
            return false;

        for (var i = digitIndex; i < code.Length; i++)
        {
            if (!char.IsAsciiDigit(code[i]))
                return false;
        }

        return true;
    }

    private static string PascalCaseFromSlug(string? slug)
    {
        if (string.IsNullOrEmpty(slug))
            return "Topic";

        var sb = new StringBuilder(slug.Length);
        foreach (var part in slug.Split('-', '_'))
        {
            if (part.Length == 0)
                continue;

            sb.Append(char.ToUpperInvariant(part[0]));
            sb.Append(part[1..]);
        }

        return sb.ToString().ToValidClassName();
    }
    #endregion

    #region adopting existing levels
    /// <summary>
    /// The heading ids are the stored link between markdown and symbols, but they only live in the
    /// content repository. If a sync's write-back never got committed there, the headings come back
    /// without ids while their level symbols already exist. Matching those by title is a one-time
    /// back-compat step: the adopted id is written back, so later runs match by id again.
    /// </summary>
    private static int AdoptExistingLevels(List<Topic> topics, EditableSymbolProject skillsProject)
    {
        var claimed = new HashSet<Guid>();
        foreach (var topic in topics)
        {
            foreach (var level in topic.Levels)
            {
                if (level is { Symbol: not null, Tour: not null })
                    claimed.Add(level.Symbol.Id);
            }
        }

        var adoptedCount = 0;
        var candidates = new List<Symbol>();
        foreach (var topic in topics)
        {
            if (topic.Namespace == null)
                continue;

            foreach (var level in topic.Levels)
            {
                if (level.Symbol != null || level.Tour == null)
                    continue;

                candidates.Clear();
                var nameSuffix = level.Title.Replace(" ", "").ToValidClassName();
                foreach (var symbol in skillsProject.Symbols.Values)
                {
                    if (symbol.Namespace != topic.Namespace || claimed.Contains(symbol.Id))
                        continue;

                    if (MatchesLevelTitle(symbol, level.Title, nameSuffix))
                        candidates.Add(symbol);
                }

                if (candidates.Count == 0)
                    continue;

                if (candidates.Count > 1)
                {
                    // Creating a stub here would add yet another copy - leave it to the user to pick one.
                    Log.Warning($"Level '{level.Title}' in {topic.Code} has no id and matches {candidates.Count} existing levels " +
                                $"({string.Join(", ", candidates.Select(c => c.Name))}) - add the id to the heading manually.");
                    level.IsAmbiguous = true;
                    continue;
                }

                var adopted = candidates[0];
                claimed.Add(adopted.Id);
                level.Symbol = adopted;
                level.NeedsIdWriteBack = true;
                adoptedCount++;
                Log.Debug($"Adopted existing level {adopted.Name} for heading '{level.Title}'");
            }

            // Adopted symbols were also appended as unlisted levels by the namespace scan - drop those copies.
            topic.Levels.RemoveAll(l => l.Tour == null && l.Symbol != null && IsListedByHeading(topic, l.Symbol));
        }

        return adoptedCount;
    }

    private static bool MatchesLevelTitle(Symbol symbol, string title, string nameSuffix)
    {
        if (symbol.TryGetSymbolUi(out var symbolUi)
            && string.Equals(symbolUi.Description.Trim(), title, StringComparison.OrdinalIgnoreCase))
            return true;

        // Level symbols are named "<code><letter>_<TitleWithoutSpaces>"
        var underscoreIndex = symbol.Name.IndexOf('_');
        return underscoreIndex > 0
               && string.Equals(symbol.Name[(underscoreIndex + 1)..], nameSuffix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsListedByHeading(Topic topic, Symbol symbol)
    {
        foreach (var level in topic.Levels)
        {
            if (level.Tour != null && level.Symbol == symbol)
                return true;
        }

        return false;
    }
    #endregion

    #region stub creation
    private static int CreateMissingStubs(List<Topic> topics, EditableSymbolProject skillsProject, SymbolUi editUi)
    {
        var stubCount = 0;
        foreach (var topic in topics)
        {
            if (topic.Namespace == null)
                continue;

            var nextLetter = GetNextFreeLevelLetter(skillsProject, topic);
            foreach (var level in topic.Levels)
            {
                if (level.Symbol != null || level.Tour == null || level.IsAmbiguous)
                    continue;

                if (nextLetter > 'z')
                {
                    Log.Warning($"Topic {topic.Code} has no free level letters left - skipping '{level.Title}'.");
                    continue;
                }

                var symbolName = $"{topic.Code}{nextLetter}_{level.Title.Replace(" ", "").ToValidClassName()}";
                var newSymbol = Duplicate.DuplicateAsNewType(editUi, skillsProject, TemplateSymbolId,
                                                             symbolName, topic.Namespace, level.Title, Vector2.Zero);
                if (newSymbol == null)
                {
                    Log.Warning($"Failed to create stub level '{symbolName}' in {topic.Namespace}.");
                    continue;
                }

                level.Symbol = newSymbol;
                level.IsNewStub = true;
                level.NeedsIdWriteBack = true;
                nextLetter++;
                stubCount++;
                Log.Debug($"Created stub level {topic.Namespace}.{symbolName}");
            }
        }

        return stubCount;
    }

    private static char GetNextFreeLevelLetter(EditableSymbolProject skillsProject, Topic topic)
    {
        var maxLetter = (char)('a' - 1);
        var prefixLength = topic.Code.Length;
        foreach (var symbol in skillsProject.Symbols.Values)
        {
            if (symbol.Namespace != topic.Namespace)
                continue;

            var name = symbol.Name;
            if (name.Length <= prefixLength + 1
                || !name.StartsWith(topic.Code, StringComparison.OrdinalIgnoreCase)
                || name[prefixLength + 1] != '_')
                continue;

            var letter = char.ToLowerInvariant(name[prefixLength]);
            if (letter > maxLetter)
                maxLetter = letter;
        }

        return (char)(maxLetter + 1);
    }
    #endregion

    #region markdown write-back
    private static void WriteBackNewIds(List<Topic> topics)
    {
        foreach (var topic in topics)
        {
            if (topic.MdPath == null)
                continue;

            var additions = new List<(string Title, string ShortId)>();
            foreach (var level in topic.Levels)
            {
                if (level is { NeedsIdWriteBack: true, Symbol: not null })
                    additions.Add((level.Title, level.Symbol.Id.ShortenGuid()));
            }

            if (additions.Count == 0)
                continue;

            try
            {
                AppendIdsToHeadings(topic.MdPath, additions);
            }
            catch (Exception e)
            {
                Log.Warning($"Failed to write ids back to {topic.MdPath}: {e.Message}");
            }
        }
    }

    private static void AppendIdsToHeadings(string mdPath, List<(string Title, string ShortId)> additions)
    {
        var bytes = File.ReadAllBytes(mdPath);
        var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        var text = Encoding.UTF8.GetString(bytes, hasBom ? 3 : 0, bytes.Length - (hasBom ? 3 : 0));

        var lines = text.Split('\n');
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex];
            var hadCr = line.EndsWith('\r');
            var content = hadCr ? line[..^1] : line;
            if (!content.StartsWith("# "))
                continue;

            var headingTitle = content[2..].Trim();
            foreach (var (title, shortId) in additions)
            {
                if (!string.Equals(headingTitle, title, StringComparison.Ordinal))
                    continue;

                lines[lineIndex] = $"# {title}  &{shortId}" + (hadCr ? "\r" : string.Empty);
                break;
            }
        }

        File.WriteAllText(mdPath, string.Join('\n', lines), new UTF8Encoding(hasBom));
    }
    #endregion

    #region markdown export
    /// <summary>
    /// Replaces the sections of the given level headings (by their order among the file's <c># </c> headings)
    /// and keeps everything else, including the blank lines that separate sections. Returns false if nothing changed.
    /// </summary>
    private static bool ReplaceLevelSections(string mdPath, Dictionary<int, string> sectionsByHeadingIndex)
    {
        var bytes = File.ReadAllBytes(mdPath);
        var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        var text = Encoding.UTF8.GetString(bytes, hasBom ? 3 : 0, bytes.Length - (hasBom ? 3 : 0));
        var useCrLf = text.Contains("\r\n");

        var lines = text.Replace("\r\n", "\n").Split('\n');
        var headingLineIndices = new List<int>();
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            if (lines[lineIndex].StartsWith("# "))
                headingLineIndices.Add(lineIndex);
        }

        var result = new List<string>(lines.Length);
        var firstHeadingLine = headingLineIndices.Count > 0 ? headingLineIndices[0] : lines.Length;
        for (var lineIndex = 0; lineIndex < firstHeadingLine; lineIndex++)
        {
            result.Add(lines[lineIndex]);
        }

        for (var headingIndex = 0; headingIndex < headingLineIndices.Count; headingIndex++)
        {
            var start = headingLineIndices[headingIndex];
            var end = headingIndex + 1 < headingLineIndices.Count ? headingLineIndices[headingIndex + 1] : lines.Length;

            if (!sectionsByHeadingIndex.TryGetValue(headingIndex, out var section))
            {
                for (var lineIndex = start; lineIndex < end; lineIndex++)
                {
                    result.Add(lines[lineIndex]);
                }

                continue;
            }

            var trailingBlankCount = 0;
            while (end - 1 - trailingBlankCount > start && lines[end - 1 - trailingBlankCount].Trim().Length == 0)
            {
                trailingBlankCount++;
            }

            result.AddRange(section.Replace("\r\n", "\n").TrimEnd().Split('\n'));
            for (var i = 0; i < trailingBlankCount; i++)
            {
                result.Add(string.Empty);
            }
        }

        var newText = string.Join(useCrLf ? "\r\n" : "\n", result);
        if (string.Equals(newText, text, StringComparison.Ordinal))
            return false;

        File.WriteAllText(mdPath, newText, new UTF8Encoding(hasBom));
        return true;
    }
    #endregion

    #region tour import
    private static int ApplyTourData(List<Topic> topics)
    {
        var appliedCount = 0;
        var keptLevelNames = new List<string>();
        foreach (var topic in topics)
        {
            foreach (var level in topic.Levels)
            {
                if (level.Symbol == null || level.Tour == null || level.Tour.TourPoints.Count == 0)
                    continue;

                if (!level.Symbol.TryGetSymbolUi(out var levelUi))
                    continue;

                // Without knowing which side changed last, an existing tour may hold edits (styles,
                // targets, ids) the markdown doesn't have. Only fill levels that have nothing to lose.
                if (!level.IsNewStub && levelUi.TourPoints.Count > 0)
                {
                    if (!TourContentMatches(levelUi.TourPoints, level.Tour.TourPoints))
                        keptLevelNames.Add(level.Symbol.Name);

                    continue;
                }

                TourDataMarkdownExport.ApplyTourDataToSymbolUi(level.Tour, levelUi);
                appliedCount++;
            }
        }

        if (keptLevelNames.Count > 0)
        {
            Log.Warning($"SkillQuest import kept {keptLevelNames.Count} existing tours that differ from the markdown " +
                        $"(export them or clear their tour points to import): {string.Join(", ", keptLevelNames)}");
        }

        return appliedCount;
    }

    private static bool TourContentMatches(List<TourPoint> existing, List<TourPoint> imported)
    {
        if (existing.Count != imported.Count)
            return false;

        for (var i = 0; i < existing.Count; i++)
        {
            if (existing[i].Style != imported[i].Style
                || !string.Equals(existing[i].Description.Trim(), imported[i].Description.Trim(), StringComparison.Ordinal))
                return false;
        }

        return true;
    }
    #endregion

    #region section layout
    private static void BuildSections(SymbolUi editUi, List<Topic> topics)
    {
        // Group by category, keep alphabetical order for deterministic layout
        var categories = new SortedDictionary<string, List<Topic>>(StringComparer.OrdinalIgnoreCase);
        foreach (var topic in topics)
        {
            if (!categories.TryGetValue(topic.CategoryKey, out var list))
            {
                list = [];
                categories[topic.CategoryKey] = list;
            }

            list.Add(topic);
        }

        // Anchor the generated block at the top-left of the previously generated categories
        // (or the origin), then lay everything out sequentially from computed sizes. Positions
        // are fully re-derived each run - guessed or stale positions caused overlaps.
        var anchor = Vector2.Zero;
        var anchorFound = false;
        foreach (var (categoryKey, _) in categories)
        {
            var existing = FindSectionByTitle(editUi, categoryKey, Guid.Empty);
            if (existing == null)
                continue;

            anchor = anchorFound ? Vector2.Min(anchor, existing.PosOnCanvas) : existing.PosOnCanvas;
            anchorFound = true;
        }

        var categoryX = anchor.X;
        foreach (var (categoryKey, categoryTopics) in categories)
        {
            var categorySection = FindSectionByTitle(editUi, categoryKey, Guid.Empty)
                                  ?? CreateSection(editUi, categoryKey, Guid.Empty);
            NormalizeSectionLabel(categorySection, categoryKey);

            // Category height derives from its tallest topic
            var maxSlotCount = 1;
            foreach (var topic in categoryTopics)
            {
                var slotCount = 0;
                foreach (var level in topic.Levels)
                {
                    if (level.Symbol != null)
                        slotCount++;
                }

                if (slotCount > maxSlotCount)
                    maxSlotCount = slotCount;
            }

            var topicHeight = HeaderHeight + Padding + maxSlotCount * RowHeight + Padding;
            categorySection.PosOnCanvas = new Vector2(categoryX, anchor.Y);
            categorySection.Size = new Vector2(Padding * 2 + categoryTopics.Count * (TopicWidth + TopicGap) - TopicGap,
                                               HeaderHeight + Padding + topicHeight + Padding);

            for (var topicIndex = 0; topicIndex < categoryTopics.Count; topicIndex++)
            {
                var topic = categoryTopics[topicIndex];
                var topicTitle = topic.Namespace?.Split('.')[^1] ?? topic.Code;
                var topicSection = FindSectionByTitle(editUi, topicTitle, categorySection.Id)
                                   ?? CreateSection(editUi, topicTitle, categorySection.Id);
                NormalizeSectionLabel(topicSection, topicTitle);

                topicSection.PosOnCanvas = categorySection.PosOnCanvas
                                           + new Vector2(Padding + topicIndex * (TopicWidth + TopicGap),
                                                         HeaderHeight + Padding);

                var slotIndex = 0;
                foreach (var level in topic.Levels)
                {
                    if (level.Symbol == null)
                        continue;

                    var childUi = FindOrCreateInstance(editUi, level.Symbol);
                    childUi.PosOnCanvas = topicSection.PosOnCanvas + new Vector2(Padding, HeaderHeight + Padding + slotIndex * RowHeight);
                    childUi.SectionId = topicSection.Id;
                    slotIndex++;
                }

                topicSection.Size = new Vector2(TopicWidth, topicHeight);
            }

            categoryX += categorySection.Size.X + CategoryGap;
        }

        SectionTree.UpdateCollapsedVisibility(editUi);
    }

    /// <summary>Sections generated by earlier sync versions carried the name in Title - move it to Label.</summary>
    private static void NormalizeSectionLabel(Section section, string label)
    {
        section.Label = label;
        if (string.Equals(section.Title, label, StringComparison.OrdinalIgnoreCase))
            section.Title = string.Empty;
    }

    private static Section? FindSectionByTitle(SymbolUi editUi, string label, Guid parentId)
    {
        foreach (var section in editUi.Sections.Values)
        {
            if (section.ParentSectionId != parentId)
                continue;

            // Older generated sections carried the name in Title - keep matching those to avoid duplicates
            if (string.Equals(section.Label, label, StringComparison.OrdinalIgnoreCase)
                || string.Equals(section.Title, label, StringComparison.OrdinalIgnoreCase))
                return section;
        }

        return null;
    }

    private static Section CreateSection(SymbolUi editUi, string label, Guid parentId)
    {
        var section = new Section
                          {
                              Id = Guid.NewGuid(),
                              Label = label,
                              ParentSectionId = parentId,
                          };
        editUi.Sections[section.Id] = section;
        return section;
    }

    private static SymbolUi.Child FindOrCreateInstance(SymbolUi editUi, Symbol symbol)
    {
        foreach (var childUi in editUi.ChildUis.Values)
        {
            if (childUi.SymbolChild.Symbol.Id == symbol.Id)
                return childUi;
        }

        return editUi.AddChild(symbol, Guid.NewGuid(), Vector2.Zero, SymbolUi.Child.DefaultOpSize);
    }
    #endregion

    private sealed class Topic
    {
        public required string Code;
        public required string CategoryKey;
        public string? Namespace;
        public string? MdPath;
        public string? MdFolderName;
        public string? NamespaceSlug;
        public readonly List<Level> Levels = [];
    }

    private sealed class Level
    {
        public TourDataMarkdownExport.TourWithId? Tour;
        public Symbol? Symbol;
        public string Title = string.Empty;
        public int HeadingIndex = -1;
        public bool IsNewStub;
        public bool NeedsIdWriteBack;
        public bool IsAmbiguous;
    }

    /// <summary>The Skills._.EditSkillQuest symbol holding the generated overview structure.</summary>
    private static readonly Guid EditSymbolId = new("6d84debe-0ebb-4d8e-a774-f00d5daa056c");

    /// <summary>The Skills._.X04x_Template symbol duplicated for stub levels.</summary>
    private static readonly Guid TemplateSymbolId = new("c19a5ea6-cb61-4ceb-88d1-8ba7a90a1963");

    // Canvas-space layout constants (no UiScaleFactor - canvas units)
    private const float Padding = 15;
    private const float HeaderHeight = MagGraphItem.LineHeight;
    private const float RowHeight = MagGraphItem.LineHeight;
    private const float TopicWidth = MagGraphItem.Width + 2 * Padding;
    private const float TopicGap = 20;
    private const float CategoryGap = 40;
}
