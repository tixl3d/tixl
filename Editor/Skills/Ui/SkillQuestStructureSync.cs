#nullable enable
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

namespace T3.Editor.Skills.Ui;

/// <summary>
/// Syncs the SkillQuest level structure from the content repository (a checkout of
/// github.com/tixl3d/skillquest) into the Skills package:
///
/// - Builds category/topic sections with all level ops inside the EditSkillQuest symbol.
/// - Matches markdown level headings to symbols via the shortened symbol id (<c># Title &amp;shortId</c>).
/// - Creates stub level symbols (duplicated from the template) for headings without an id
///   and writes the new id back into the markdown, so re-running is idempotent.
/// - Applies the markdown tour points to every matched level — the repository is authoritative
///   for tour content; symbols whose markdown section has no tour points are left untouched.
/// </summary>
internal static class SkillQuestStructureSync
{
    internal static void SyncFromRepository()
    {
        if (!TryFindRepositoryFolder(out var repositoryPath))
        {
            Log.Warning("Can't find skillquest content repository. Set SkillQuestRepositoryPath in user settings " +
                        "or place a 'skillquest' checkout next to the TiXL folder.");
            return;
        }

        EditableSymbolProject? skillsProject = null;
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
            return;
        }

        if (!SymbolUiRegistry.TryGetSymbolUi(EditSymbolId, out var editUi))
        {
            Log.Warning("Can't find EditSkillQuest symbol in Skills package.");
            return;
        }

        Log.Debug($"Syncing SkillQuest structure from {repositoryPath}...");

        var topics = CollectTopics(repositoryPath, skillsProject);
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

        Log.Debug($"SkillQuest sync: {topics.Count} topics, {levelCount} levels, {stubCount} stubs created, {tourCount} tours applied.");
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
                foreach (var tour in tours)
                {
                    var level = new Level { Tour = tour, Title = tour.Title };
                    if (!string.IsNullOrEmpty(tour.IdString))
                    {
                        if (symbolsByShortId.TryGetValue(tour.IdString, out var symbol))
                        {
                            level.Symbol = symbol;
                        }
                        else
                        {
                            Log.Warning($"Level '{tour.Title}' in {cleanName} references unknown id &{tour.IdString} - skipping.");
                            continue;
                        }
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
                if (level.Symbol != null || level.Tour == null)
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
                if (level is { IsNewStub: true, Symbol: not null })
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

    #region tour import
    private static int ApplyTourData(List<Topic> topics)
    {
        var appliedCount = 0;
        foreach (var topic in topics)
        {
            foreach (var level in topic.Levels)
            {
                if (level.Symbol == null || level.Tour == null || level.Tour.TourPoints.Count == 0)
                    continue;

                if (!level.Symbol.TryGetSymbolUi(out var levelUi))
                    continue;

                TourDataMarkdownExport.ApplyTourDataToSymbolUi(level.Tour, levelUi);
                appliedCount++;
            }
        }

        return appliedCount;
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
        public bool IsNewStub;
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
