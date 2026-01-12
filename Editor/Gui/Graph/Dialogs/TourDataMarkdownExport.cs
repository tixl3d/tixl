#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;
using ImGuiNET;
using T3.Core.Utils;
using T3.Editor.UiModel;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.Graph.Dialogs;

/// <summary>
/// Write or read tours as Markdown to improve the editing flow.
/// </summary>
/// <remarks>
/// While creating levels and topics of the SkillQuest we noticed that it would be useful to
/// write or export multiple tours into a single Markdown document. With h1 (#) for Levels and
/// h2 (##) for tour points. We could then use external software or LLMs to do things like spell
/// checking and just "paste back" the updated data.
///
/// Things got a little out of hand because the idea of using shorted ids to keep references
/// turned out to be much more complex than expected.
///
/// To test this function, open AppMenu → TiXL → Development → Tour Point Editor.
/// </remarks>
internal static partial class TourDataMarkdownExport
{
    #region writing markdown
    
    internal static void WriteSymbolTourMarkdown(SymbolUi symbolUi, StringBuilder? sb = null)
    {
        sb ??= new StringBuilder();
        
        sb.Append("# ");
        sb.Append(string.IsNullOrEmpty(symbolUi.Description) 
                      ? symbolUi.Symbol.Name 
                      : symbolUi.Description);
        
        sb.Append("  &");
        sb.AppendLine(symbolUi.Symbol.Id.ShortenGuid());
        sb.AppendLine();
        
        foreach (var tp in symbolUi.TourPoints)
        {
            tp.ToMarkdown(sb, symbolUi);
        }
    }    
    
    public static void ToMarkdown(this TourPoint tourPoint, StringBuilder sb, SymbolUi symbolUi)
    {
        // Type with reference to child and input
        sb.Append($"## {tourPoint.Style}");
        if (tourPoint.ChildId != Guid.Empty 
            && TryGetChildNameIndex(symbolUi, tourPoint.ChildId, out var childName) )
        {
            TryGetInputName(symbolUi, tourPoint, out var inputName);
            
            sb.Append("(");
            sb.Append(childName);
            if (!string.IsNullOrEmpty(inputName))
            {
                sb.Append('.');
                sb.Append(inputName);
            }
            sb.Append(") ");
        }
        
        // Write id
        sb.Append(" &");
        sb.Append(tourPoint.Id.ShortenGuid());
        sb.AppendLine();
        sb.AppendLine(tourPoint.Description.Replace("\n", "\n\n"));
        sb.AppendLine();
    }

    private static bool TryGetChildNameIndex(SymbolUi symbolUi, Guid id, out string name)
    {
        name = "FAIL?";

        if (!symbolUi.ChildUis.TryGetValue(id, out var child))
        {
            return false;
        }

        // find identical children and order them by height / pos x
        var childSymbolId = child.SymbolChild.Symbol.Id;
        var index = symbolUi.ChildUis.Values
                            .Where(c => c.SymbolChild.Symbol.Id == childSymbolId && c.CollapsedIntoAnnotationFrameId == Guid.Empty)
                            .OrderBy(c => c.PosOnCanvas.Y + c.PosOnCanvas.X * 0.1f)
                            .Select((item, idx) => new { Item = item, Index = idx })
                            .FirstOrDefault(pair => pair.Item.Id == child.Id)?.Index ?? -1;

        if (index == -1)
        {
            return false;
        }

        if (index == 0)
        {
            name = child.SymbolChild.Symbol.Name;
        }
        else
        {
            name = $"{child.SymbolChild.Symbol.Name}:{index + 1}";
        }
        
        return true;
    }

    private static bool TryGetInputName(SymbolUi symbolUi, TourPoint tourPoint, out string inputName)
    {
        inputName = string.Empty;

        if (tourPoint.InputId == Guid.Empty || tourPoint.ChildId == Guid.Empty)
            return false;

        if (!symbolUi.Symbol.Children.TryGetValue(tourPoint.ChildId, out var child))
            return false;

        var input = child.Symbol.InputDefinitions.FirstOrDefault(i => i.Id == tourPoint.InputId);
        if (input == null)
            return false;

        inputName = input.Name;
        return true;
    }

    #endregion

    #region pasting from mark down
    /// <summary>
    /// Parses mark down description for tour data matching symbols and tour points.
    /// </summary>
    /// <remarks>
    /// Copy sample markdown from existing symbols with tours.
    ///
    /// There are two methods how this could be applied:
    /// - If some ops are selected, their SymbolUis will be the target
    /// - Otherwise the current compositionUi will be the target, if...
    ///     1. _one_ of the Tours has a matching SymbolId
    ///     2. There is a single Tour _without_ SymbolId
    ///
    /// Sadly, dealing with shorted Ids is annoying. 
    /// </remarks>
    internal static bool TryPasteTourData(SymbolUi compositionUi, ProjectView projectView)
    {
        var markdown = ImGui.GetClipboardText();
        var tours = GetToursFromMarkdown(markdown);

        if (tours.Count == 0)
        {
            Log.Warning("No tour points found in clipboard");
            return false;
        }

        if (!projectView.NodeSelection.IsAnythingSelected())
        {
            if (tours.Count > 1)
            {
                var shortCompositionId = compositionUi.Symbol.Id.ShortenGuid();
                foreach (var tour in tours)
                {
                    if (tour.IdString == shortCompositionId)
                    {
                        ApplyTourDataToSymbolUi(tour, compositionUi);
                        return true;
                    }
                }

                Log.Warning("Can't find tour matching current composition");
                return false;
            }

            var newTour = tours[0];
            if (!string.IsNullOrEmpty(newTour.IdString) && compositionUi.Symbol.Id.ShortenGuid() != newTour.IdString)
            {
                Log.Warning("TourId doesn't match current composition.");
                return false;
            }

            ApplyTourDataToSymbolUi(newTour, compositionUi);
        }
        else
        {
            var selectedChildUis = projectView.NodeSelection.GetSelectedChildUis().ToArray();

            if (tours.Count == 1 && selectedChildUis.Length == 1)
            {
                var tour = tours[0];
                var targetChildUi = selectedChildUis[0];
                if (!string.IsNullOrEmpty(tour.IdString) && tour.IdString != targetChildUi.SymbolChild.Symbol.Id.ShortenGuid())
                {
                    Log.Warning("TourId doesn't match current composition.");
                    return false;
                }
                
                if (!targetChildUi.SymbolChild.Symbol.TryGetSymbolUi(out var targetSymbolUi))
                {
                    Log.Warning($"Can't find target for {tour.IdString} to paste data.");
                    return false;
                }
                
                ApplyTourDataToSymbolUi(tour, targetSymbolUi);
            }
            else
            {
                foreach (var tour in tours)
                {
                    var idStringMissing = string.IsNullOrEmpty(tour.IdString);
                    if (idStringMissing)
                    {
                        Log.Warning($"Can't paste data of {tour.Title} to selection with without tourIds.");
                        continue;
                    }

                    var targetChildUi = selectedChildUis
                                       .Where(c => c.CollapsedIntoAnnotationFrameId == Guid.Empty)
                                       .OrderByDescending(c => c.PosOnCanvas.Y + c.PosOnCanvas.X * 0.1f)
                                       .FirstOrDefault(c => string.Equals(tour.IdString, c.SymbolChild.Symbol.Id.ShortenGuid(),
                                                                          StringComparison.InvariantCulture));

                    if (targetChildUi == null || !targetChildUi.SymbolChild.Symbol.TryGetSymbolUi(out var targetSymbolUi))
                    {
                        Log.Warning($"Can't find target for {tour.IdString} to paste data.");
                        continue;
                    }

                    ApplyTourDataToSymbolUi(tour, targetSymbolUi);
                }
            }
        }

        return false;
    }

    private static void ApplyTourDataToSymbolUi(TourWithId tour, SymbolUi targetSymbolUi)
    {
        RealizeTourIds(tour, targetSymbolUi);
        if (!string.IsNullOrEmpty(tour.Title))
            targetSymbolUi.Description = tour.Title;
        
        targetSymbolUi.TourPoints.Clear();
        targetSymbolUi.TourPoints.AddRange(tour.TourPoints);
        targetSymbolUi.FlagAsModified();
        Log.Debug($"Pasted {tour.TourPoints.Count} tour points to {targetSymbolUi}");
    }

    private static void RealizeTourIds(TourWithId tour, SymbolUi targetSymbolUi)
    {
        foreach (var tourPoint in tour.TourPoints)
        {
            RealizeIdsTourPointIds(tourPoint, targetSymbolUi);
        }
    }

    private static List<TourWithId> GetToursFromMarkdown(string markdown)
    {
        _tours.Clear();
        var span = markdown.AsSpan();
        _lineIndex = 0;
        var i = 0;

        while (i < span.Length)
        {
            var lineStart = i;

            // find end of line
            while (i < span.Length && span[i] != '\n')
                i++;

            var line = span.Slice(lineStart, i - lineStart);
            _lineIndex++;

            // handle CRLF
            if (line.Length > 0 && line[^1] == '\r')
                line = line[..^1];

            if (line.StartsWith("# ".AsSpan()))
            {
                TryStartTour(line[2..]);
            }
            else if (line.StartsWith("## ".AsSpan()))
            {
                TryStartTourPoint(line[3..]);
            }
            else if (line.IsEmpty)
            {
                _lineBreakPending = true;
            }
            else
            {
                if (_activeTourPoint != null)
                {
                    if (_activeTourPointDescriptionSb.Length > 0)
                    {
                        _activeTourPointDescriptionSb.Append(!_lineBreakPending ? ' ' : '\n');
                    }

                    _lineBreakPending = false;
                    _activeTourPointDescriptionSb.Append(line);
                }
            }

            i++; // skip '\n'
        }

        CompleteActiveTourPoint();

        return _tours;
    }

    private static void CompleteActiveTourPoint()
    {
        if (_activeTourPoint == null)
            return;

        _lineBreakPending = false;
        _activeTourPoint.Description = _activeTourPointDescriptionSb.ToString();
        _activeTourPoint = null;
        _activeTourPointDescriptionSb.Clear();
    }

    private static void TryStartTour(ReadOnlySpan<char> line)
    {
        CompleteActiveTourPoint();
        _activeTourPointDescriptionSb.Clear();

        TryGetIdString(line, out var idString, out var title);

        _activeTour = new TourWithId(idString.ToString(), title.Trim().ToString(), []);
        _tours.Add(_activeTour);
    }

    private static void TryStartTourPoint(ReadOnlySpan<char> line)
    {
        CompleteActiveTourPoint();
        _activeTourPointDescriptionSb.Clear();

        if (_activeTour == null)
        {
            _activeTourPoint = null;
            return;
        }

        TryGetIdString(line, out var idString, out var styleAndTargetSpan);

        var end = 0;
        while (end < styleAndTargetSpan.Length && styleAndTargetSpan[end].IsAlphaNumericOrUnderscore())
            end++;

        var title = styleAndTargetSpan[..end];

        if (!Enum.TryParse<TourPoint.Styles>(title, out var style))
        {
            Log.Warning($"Can't parse tour point style {style}");
        }

        _activeTourPoint = new TourPoint
                               {
                                   Style = style,
                                   ShortId = idString.ToString(),
                                   StyleAndTargetString = styleAndTargetSpan.ToString(),
                               };

        _activeTour.TourPoints.Add(_activeTourPoint);
    }

    static bool IsAlphaNumericOrUnderscore(this char c) =>
        (c >= 'A' && c <= 'Z') ||
        (c >= 'a' && c <= 'z') ||
        (c >= '0' && c <= '9') ||
        c == '_';

    /// <summary>
    /// Try to find the referenced items in the target symbolUi.
    /// </summary>
    private static void RealizeIdsTourPointIds(TourPoint tourPoint, SymbolUi symbolUi)
    {
        // Copy original ids if possible
        var foundId = false;
        foreach (var orgTourPoint in symbolUi.TourPoints)
        {
            if (tourPoint.ShortId != orgTourPoint.Id.ShortenGuid())
                continue;

            tourPoint.Id = orgTourPoint.Id;
            foundId = true;
            break;
        }
        
        if(!foundId)
            tourPoint.Id = Guid.NewGuid();

        // e.g.  Info(Symbol:1)
        var symbolName = string.Empty;
        var inputName = string.Empty;
        var index = 1;

        var m = MatchSymbolNameAndIndex().Match(tourPoint.StyleAndTargetString);
        if (m.Success)
        {
            int titleGroup = 1;
            int indexGroup = 2;
            int inputNameGroup = 3;
            
            symbolName = m.Groups[titleGroup].Value;
            if (m.Groups[indexGroup].Success && int.TryParse(m.Groups[indexGroup].Value, out var i))
                index = i;
            
            if (m.Groups[inputNameGroup].Success)
                inputName = m.Groups[inputNameGroup].Value;
        }

        if (!string.IsNullOrEmpty(symbolName))
        {
            var matchIndex = 0;
            foreach (SymbolUi.Child childUi in symbolUi.ChildUis.Values)
            {
                if (childUi.CollapsedIntoAnnotationFrameId != Guid.Empty)
                    continue;

                var childSymbolName = childUi.SymbolChild.Symbol.Name;
                if (!string.Equals(childSymbolName, symbolName, StringComparison.InvariantCulture))
                    continue;

                matchIndex++;

                if (matchIndex < index)
                    continue;

                tourPoint.ChildId = childUi.Id;

                if (!string.IsNullOrEmpty(inputName))
                {
                    foreach (var input in childUi.SymbolChild.Symbol.InputDefinitions)
                    {
                        if (input.Name != inputName) 
                            continue;
                        
                        tourPoint.InputId = input.Id;
                        break;
                    }
                }
                
                break;
            }
        }
    }

    // Find symbol id " &abc1234"
    private static void TryGetIdString(ReadOnlySpan<char> line, out ReadOnlySpan<char> id, out ReadOnlySpan<char> title)
    {
        id = ReadOnlySpan<char>.Empty;
        title = line;

        var idStartIndex = line.IndexOf(" &");
        if (idStartIndex == -1)
            return;

        var maxLength = 7;
        if (line.Length < idStartIndex + maxLength + 2)
            return;

        if (line[idStartIndex + 2] == ' ')
            return;

        var length = 0;
        while (length < maxLength && line[idStartIndex + 2 + length] != ' ')
            length++;

        id = line.Slice(idStartIndex + 2, length);
        title = line[..idStartIndex].Trim();
    }

    private sealed record TourWithId(string IdString, string Title, List<TourPoint> TourPoints);

    private static readonly List<TourWithId> _tours = [];
    private static TourWithId? _activeTour;

    private static readonly StringBuilder _activeTourPointDescriptionSb = new();
    private static bool _lineBreakPending;

    private static TourPoint? _activeTourPoint;
    private static int _lineIndex;
    
    //[GeneratedRegex(@"\((?<title>[^):]+)(?::(?<index>\d+))?\)", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    [GeneratedRegex(@"\(([^):.]+)(?::(\d+))?(?:\.([^)]+))?\)", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex MatchSymbolNameAndIndex();
    #endregion
}