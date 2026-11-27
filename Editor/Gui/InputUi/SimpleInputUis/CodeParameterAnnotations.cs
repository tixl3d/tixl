#nullable enable
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ImGuiNET;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.UiModel;

namespace T3.Editor.Gui.InputUi.SimpleInputUis;

/// <summary>
/// Labels, slider ranges and tooltips that a code snippet gives the generic inputs of its op (A, B, C, D, Offset, ...) with
/// comment lines like <c>// A: Scale 0..1 - size of each point</c>. They come with the code, so a preset arrives already
/// labelled. Only the parameter window shows them; the inputs keep their names.
/// </summary>
/// <remarks>
/// One annotation per line: <c>// InputName: Label [range] [- tooltip]</c>, the range being <c>min..max</c> for a soft range
/// or <c>[min..max]</c> to clamp. Several inputs can share a line (<c>// A,B: 0..1</c>), and <c>// C,D: -</c> marks inputs the
/// code doesn't use. Lines that don't match are ignored, so half-typed annotations never warn.
/// </remarks>
internal sealed partial class CodeParameterAnnotations
{
    /// <param name="ButtonLabel">"Label (InputName)" with the parameter name button's id appended, built once; empty without a label.</param>
    /// <param name="IsUnused">Marked with "-": the code doesn't read the input. It is labelled "- (B)" rather than dimmed, because
    /// dimming already means the value is at its default.</param>
    public readonly record struct Entry(string InputName, string Label, string ButtonLabel, bool HasRange, float Min, float Max, bool Clamp,
                                        string Tooltip, bool IsUnused);

    public static readonly CodeParameterAnnotations Empty = new([]);

    public IReadOnlyList<Entry> Entries => _entries;

    public bool TryGet(string inputName, out Entry entry)
    {
        foreach (var candidate in _entries)
        {
            if (!string.Equals(candidate.InputName, inputName, StringComparison.OrdinalIgnoreCase))
                continue;

            entry = candidate;
            return true;
        }

        entry = default;
        return false;
    }

    public static CodeParameterAnnotations Parse(string code)
    {
        var entries = new List<Entry>();
        foreach (Match match in LineRegex().Matches(code))
        {
            var description = match.Groups["description"].Value.Trim();
            if (description == "-" || description.StartsWith("- ", StringComparison.Ordinal))
            {
                AddForEachName(entries, match.Groups["names"].Value, "-", false, 0, 0, false, description[1..].Trim(), isUnused: true);
                continue;
            }

            var tooltip = string.Empty;
            var tooltipStart = description.IndexOf(" - ", StringComparison.Ordinal);
            if (tooltipStart >= 0)
            {
                tooltip = description[(tooltipStart + 3)..].Trim();
                description = description[..tooltipStart].Trim();
            }

            var hasRange = false;
            var clamp = false;
            float min = 0, max = 0;
            var range = RangeRegex().Match(description);
            if (range.Success)
            {
                // An empty range like 1..1 is dropped rather than shown as part of the label.
                hasRange = float.TryParse(range.Groups["min"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out min)
                           && float.TryParse(range.Groups["max"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out max)
                           && min != max;
                clamp = hasRange && range.Groups["open"].Success && range.Groups["close"].Success;
                if (min > max)
                    (min, max) = (max, min);

                description = description[..range.Index].Trim();
            }

            if (description.Length == 0 && !hasRange && tooltip.Length == 0)
                continue;

            AddForEachName(entries, match.Groups["names"].Value, description, hasRange, min, max, clamp, tooltip, isUnused: false);
        }

        return entries.Count == 0 ? Empty : new CodeParameterAnnotations(entries);
    }

    /// <summary>
    /// The annotations in the first <see cref="StringInputUi.UsageType.Code"/> input of the op, or <see cref="Empty"/>.
    /// Asked for every parameter row, so it is resolved once per op and frame, and parsed only when the code changed.
    /// </summary>
    public static CodeParameterAnnotations GetFor(Instance instance)
    {
        var frame = ImGui.GetFrameCount();
        if (ReferenceEquals(instance, _lastInstance) && frame == _lastFrame)
            return _lastResult;

        _lastInstance = instance;
        _lastFrame = frame;
        _lastResult = Resolve(instance);
        return _lastResult;
    }

    private static CodeParameterAnnotations Resolve(Instance instance)
    {
        var symbolUi = instance.GetSymbolUi();
        foreach (var input in instance.Inputs)
        {
            if (input is not InputSlot<string> stringInput
                || !symbolUi.InputUis.TryGetValue(input.Id, out var inputUi)
                || inputUi is not StringInputUi { Usage: StringInputUi.UsageType.Code })
                continue;

            // Typed values show edits right away, even while the op isn't evaluated; a connected code input only has its evaluated value.
            var code = stringInput.HasInputConnections ? stringInput.Value : stringInput.TypedInputValue.Value;
            if (string.IsNullOrEmpty(code))
                return Empty;

            if (!_parsed.TryGetValue(code, out var annotations))
            {
                annotations = Parse(code);
                _parsed.AddOrUpdate(code, annotations);
            }

            return annotations;
        }

        return Empty;
    }

    /** The first annotation of an input wins, so a later line can't silently relabel it. */
    private static void AddForEachName(List<Entry> entries, string names, string label, bool hasRange, float min, float max, bool clamp,
                                       string tooltip, bool isUnused)
    {
        foreach (var rawName in names.Split(','))
        {
            var inputName = rawName.Trim();
            if (inputName.Length == 0 || Contains(entries, inputName))
                continue;

            // The input's own name stays visible, because the code refers to it by that.
            var buttonLabel = label.Length > 0 && !string.Equals(label, inputName, StringComparison.OrdinalIgnoreCase)
                                  ? $"{label} ({inputName})##ParamName"
                                  : string.Empty;
            entries.Add(new Entry(inputName, label, buttonLabel, hasRange, min, max, clamp, tooltip, isUnused));
        }
    }

    private static bool Contains(List<Entry> entries, string inputName)
    {
        foreach (var entry in entries)
        {
            if (string.Equals(entry.InputName, inputName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private CodeParameterAnnotations(List<Entry> entries)
    {
        _entries = entries;
    }

    [GeneratedRegex(@"^[ \t]*//[ \t]*(?<names>[A-Za-z_]\w*([ \t]*,[ \t]*[A-Za-z_]\w*)*)[ \t]*:(?<description>[^\r\n]*)", RegexOptions.Multiline)]
    private static partial Regex LineRegex();

    [GeneratedRegex(@"(?<open>\[)?\s*(?<min>-?\d*\.?\d+)\s*\.\.\s*(?<max>-?\d*\.?\d+)\s*(?<close>\])?\s*$")]
    private static partial Regex RangeRegex();

    private readonly List<Entry> _entries;

    /** Strings are immutable, so a code string maps to one result for as long as it lives. */
    private static readonly ConditionalWeakTable<string, CodeParameterAnnotations> _parsed = new();

    private static Instance? _lastInstance;
    private static int _lastFrame = -1;
    private static CodeParameterAnnotations _lastResult = Empty;
}
