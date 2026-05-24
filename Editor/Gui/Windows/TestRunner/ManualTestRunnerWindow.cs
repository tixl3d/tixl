#nullable enable
using System.Text;
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.Styling.Markdown;

namespace T3.Editor.Gui.Windows.TestRunner;

/// <summary>
/// In-editor manual test runner. Phase 1: parse <c>.tests-manual/*.md</c>,
/// pick a subset, walk through steps recording Pass/Fail/Other + comment,
/// summarize in-memory. Export buttons are stubbed in this phase.
///
/// State machine: Pick → Run → Summary → Pick. See
/// <see href="../../../../../.agentic/Plans/Plan_ManualTestRunner.md"/>.
/// </summary>
internal sealed class ManualTestRunnerWindow : Window
{
    internal ManualTestRunnerWindow()
    {
        Config.Title = "Manual Test Runner";
        MenuTitle = "Manual Test Runner...";
        WindowPaddingOverride = new Vector2(16, 14);
    }

    internal override IReadOnlyList<Window> GetInstances() => Array.Empty<Window>();

    protected override void DrawContent()
    {
        if (_allSets == null)
            ReloadSets();

        switch (_state)
        {
            case State.Pick:
                DrawPickState();
                break;
            case State.Run:
                DrawRunState();
                break;
            case State.Summary:
                DrawSummaryState();
                break;
        }
    }

    // ----- Pick state ---------------------------------------------------

    private void DrawPickState()
    {
        var sets = _allSets!;

        FormInputs.AddSectionHeader("Manual test runner");
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        ImGui.TextWrapped($"Loaded {sets.Count} test set{(sets.Count == 1 ? "" : "s")} from {_testsDir}");
        ImGui.PopStyleColor();

        FormInputs.AddVerticalSpace(6);

        // Reload + filter row.
        if (ImGui.Button("Reload"))
            ReloadSets();
        ImGui.SameLine();
        ImGui.SetNextItemWidth(220 * T3Ui.UiScaleFactor);
        ImGui.InputTextWithHint("##filter", "Filter…", ref _filter, 256);

        var allTags = CollectAllTags(sets);
        if (allTags.Count > 0)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("Tags:");
            foreach (var tag in allTags)
            {
                ImGui.SameLine();
                var active = _selectedTags.Contains(tag);
                if (active)
                {
                    ImGui.PushStyleColor(ImGuiCol.Button, UiColors.StatusActivated.Rgba);
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UiColors.StatusActivated.Rgba);
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, UiColors.StatusActivated.Rgba);
                }
                if (ImGui.SmallButton(tag))
                {
                    if (active) _selectedTags.Remove(tag);
                    else _selectedTags.Add(tag);
                }
                if (active)
                    ImGui.PopStyleColor(3);
            }
        }

        FormInputs.AddVerticalSpace(8);
        ImGui.Separator();
        FormInputs.AddVerticalSpace(4);

        // Set list.
        var visibleCount = 0;
        foreach (var set in sets)
        {
            if (!Matches(set, _filter, _selectedTags))
                continue;

            visibleCount++;
            DrawSetRow(set);
        }

        if (visibleCount == 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
            ImGui.TextWrapped(sets.Count == 0
                                  ? $"No `.md` test sets found in {_testsDir}"
                                  : "No sets match the current filter.");
            ImGui.PopStyleColor();
        }

        FormInputs.AddVerticalSpace(8);
        ImGui.Separator();
        FormInputs.AddVerticalSpace(4);

        var anySelected = _selectedSetIds.Count > 0;
        if (!anySelected)
            ImGui.BeginDisabled();
        if (ImGui.Button("Start Run"))
            StartRun();
        if (!anySelected)
            ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button("Select All"))
        {
            foreach (var s in sets)
                if (Matches(s, _filter, _selectedTags))
                    _selectedSetIds.Add(s.Id);
        }
        ImGui.SameLine();
        if (ImGui.Button("Clear"))
            _selectedSetIds.Clear();
    }

    private void DrawSetRow(TestSet set)
    {
        var selected = _selectedSetIds.Contains(set.Id);
        if (ImGui.Checkbox($"##{set.Id}", ref selected))
        {
            if (selected) _selectedSetIds.Add(set.Id);
            else _selectedSetIds.Remove(set.Id);
        }

        ImGui.SameLine();
        ImGui.PushFont(Fonts.FontBold);
        ImGui.TextUnformatted(set.Title);
        ImGui.PopFont();

        if (set.ParseWarnings.Count > 0)
        {
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.StatusWarning.Rgba);
            ImGui.TextUnformatted($"  ⚠ {set.ParseWarnings.Count} warning(s)");
            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                foreach (var w in set.ParseWarnings)
                    ImGui.TextUnformatted(w);
                ImGui.EndTooltip();
            }
        }

        // Sub-line: scope + tags + step count.
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        ImGui.PushFont(Fonts.FontSmall);
        var sb = _scratch;
        sb.Clear();
        if (!string.IsNullOrEmpty(set.Scope))
            sb.Append(set.Scope).Append(" — ");
        sb.Append(set.Steps.Count).Append(" step").Append(set.Steps.Count == 1 ? "" : "s");
        if (set.Tags.Count > 0)
        {
            sb.Append("  [");
            for (var i = 0; i < set.Tags.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(set.Tags[i]);
            }
            sb.Append(']');
        }
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 26 * T3Ui.UiScaleFactor);
        ImGui.TextUnformatted(sb.ToString());
        ImGui.PopFont();
        ImGui.PopStyleColor();
    }

    // ----- Run state ----------------------------------------------------

    private void DrawRunState()
    {
        var run = _run!;
        if (_currentSetIdx >= run.Sets.Count)
        {
            FinishRun();
            return;
        }

        var set = run.Sets[_currentSetIdx];
        if (set.Steps.Count == 0)
        {
            AdvanceStep();
            return;
        }

        var step = set.Steps[_currentStepIdx];
        var globalIdx = ComputeGlobalStepIndex();
        var globalCount = ComputeTotalStepCount();

        DrawRunHeader(set, globalIdx, globalCount);
        FormInputs.AddVerticalSpace(8);

        // Two-column layout: step content (left) + comment + outcome (right).
        var avail = ImGui.GetContentRegionAvail();
        var rightWidth = MathF.Max(220 * T3Ui.UiScaleFactor, avail.X * 0.32f);
        var leftWidth = MathF.Max(280 * T3Ui.UiScaleFactor, avail.X - rightWidth - 16 * T3Ui.UiScaleFactor);

        ImGui.BeginChild("##step-content", new Vector2(leftWidth, -1),
                         ImGuiChildFlags.None, ImGuiWindowFlags.NoBackground);
        DrawStepBody(set, step);
        ImGui.EndChild();

        ImGui.SameLine();
        ImGui.BeginChild("##step-side", new Vector2(rightWidth, -1),
                         ImGuiChildFlags.None, ImGuiWindowFlags.NoBackground);
        DrawStepSide(set, step);
        ImGui.EndChild();
    }

    private void DrawRunHeader(TestSet set, int globalIdx, int globalCount)
    {
        var startY = ImGui.GetCursorPosY();
        var iconSize = new Vector2(ImGui.GetFrameHeight(), ImGui.GetFrameHeight());

        // Title + step counter (left).
        ImGui.PushFont(Fonts.FontLarge);
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.Text.Rgba);
        ImGui.TextUnformatted(set.Title);
        ImGui.PopStyleColor();
        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        ImGui.TextUnformatted($"  {_currentStepIdx + 1}/{set.Steps.Count}");
        ImGui.PopStyleColor();
        ImGui.PopFont();

        // Right-aligned nav: ← → Abandon.
        var navWidth = iconSize.X * 2 + 6 * T3Ui.UiScaleFactor + 70 * T3Ui.UiScaleFactor;
        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetWindowWidth() - navWidth - WindowPaddingOverride.X);
        ImGui.SetCursorPosY(startY + 4 * T3Ui.UiScaleFactor);

        if (CustomComponents.IconButton(Icon.ChevronLeft, iconSize))
            StepBack();
        ImGui.SameLine(0, 0);
        if (CustomComponents.IconButton(Icon.ChevronRight, iconSize))
            AdvanceStep();
        ImGui.SameLine(0, 6);
        if (ImGui.SmallButton("Abandon"))
        {
            _state = State.Pick;
            _run = null;
        }

        // Sub-line: "Step N of M".
        ImGui.PushFont(Fonts.FontSmall);
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        ImGui.TextUnformatted($"Step {globalIdx + 1} of {globalCount}");
        ImGui.PopStyleColor();
        ImGui.PopFont();
    }

    private void DrawStepBody(TestSet set, TestStep step)
    {
        ImGui.PushFont(Fonts.FontBold);
        ImGui.TextUnformatted(step.Title);
        ImGui.PopFont();
        FormInputs.AddVerticalSpace(6);

        // Synthesize three markdown blocks (one each for context, action,
        // expected) on step transition. Three caches stay hot independently
        // so a frame on the same step is layout-free.
        if (_cachedStepKey != (set.Id, _currentStepIdx))
        {
            _cachedContext = string.IsNullOrEmpty(step.Context) ? null : step.Context;
            _cachedAction = BuildBulletMarkdown(step.ActionBullets);
            _cachedExpected = BuildBulletMarkdown(step.ExpectedBullets);
            _cachedStepKey = (set.Id, _currentStepIdx);
        }

        if (_cachedContext != null)
        {
            DrawSectionLabel("Context");
            _contextMarkdown.Draw(_cachedContext);
            FormInputs.AddVerticalSpace(4);
        }

        if (_cachedAction != null)
        {
            DrawSectionLabel("Action");
            _actionMarkdown.Draw(_cachedAction);
            FormInputs.AddVerticalSpace(4);
        }

        if (_cachedExpected != null)
        {
            DrawSectionLabel("Expected");
            _expectedMarkdown.Draw(_cachedExpected);
        }
    }

    private static void DrawSectionLabel(string label)
    {
        ImGui.PushFont(Fonts.FontSmall);
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        ImGui.TextUnformatted(label);
        ImGui.PopStyleColor();
        ImGui.PopFont();
    }

    private static string? BuildBulletMarkdown(IReadOnlyList<string> bullets)
    {
        if (bullets.Count == 0)
            return null;
        var sb = new StringBuilder();
        for (var i = 0; i < bullets.Count; i++)
        {
            sb.Append("- ").Append(bullets[i]);
            if (i + 1 < bullets.Count)
                sb.Append('\n');
        }
        return sb.ToString();
    }

    private void DrawStepSide(TestSet set, TestStep step)
    {
        var result = GetOrCreateResult(set.Id, _currentStepIdx);

        ImGui.PushFont(Fonts.FontSmall);
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        ImGui.TextUnformatted("Actual result");
        ImGui.PopStyleColor();
        ImGui.PopFont();

        var comment = result.Comment;
        var inputPos = ImGui.GetCursorScreenPos();
        if (ImGui.InputTextMultiline("##comment", ref comment, 1024,
                                      new Vector2(-1, 160 * T3Ui.UiScaleFactor)))
        {
            result.Comment = comment;
        }

        // Manual placeholder text — ImGui has no InputTextMultilineWithHint.
        if (string.IsNullOrEmpty(result.Comment) && !ImGui.IsItemActive())
        {
            var dl = ImGui.GetWindowDrawList();
            var color = ImGui.GetColorU32(UiColors.TextMuted.Fade(0.6f).Rgba);
            var pad = ImGui.GetStyle().FramePadding;
            dl.AddText(inputPos + pad, color, "Add comment…");
        }

        FormInputs.AddVerticalSpace(10);

        // Outcome buttons: bold font, rounded corners, slightly taller.
        var btnH = ImGui.GetFrameHeight() * 1.5f;
        ImGui.PushFont(Fonts.FontBold);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f * T3Ui.UiScaleFactor);

        DrawOutcomeButton("Success", UiColors.StatusControlled, btnH,
                          () => RecordOutcomeAndAdvance(result, Outcome.Pass));

        ImGui.SameLine();
        DrawOutcomeButton("Fail", UiColors.StatusWarning, btnH,
                          () => RecordOutcomeAndAdvance(result, Outcome.Fail));

        ImGui.SameLine();
        var otherEnabled = !string.IsNullOrWhiteSpace(result.Comment);
        if (!otherEnabled)
            ImGui.BeginDisabled();
        if (ImGui.Button("Other…", new Vector2(0, btnH)))
            RecordOutcomeAndAdvance(result, Outcome.Other);
        if (!otherEnabled)
            ImGui.EndDisabled();
        if (!otherEnabled && ImGui.IsItemHovered())
            CustomComponents.TooltipForLastItem("Add a comment first");

        ImGui.PopStyleVar();
        ImGui.PopFont();
    }

    private static void DrawOutcomeButton(string label, Color fill, float height, Action onClick)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, fill.Rgba);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, fill.Fade(0.85f).Rgba);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, fill.Rgba);
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.ForegroundFull.Rgba);
        if (ImGui.Button(label, new Vector2(0, height)))
            onClick();
        ImGui.PopStyleColor(4);
    }

    private void RecordOutcomeAndAdvance(StepResult result, Outcome outcome)
    {
        result.Outcome = outcome;
        result.TimestampUtc = DateTime.UtcNow;
        AdvanceStep();
    }

    private void AdvanceStep()
    {
        var run = _run!;
        if (_currentSetIdx >= run.Sets.Count)
            return;

        var set = run.Sets[_currentSetIdx];
        if (_currentStepIdx + 1 < set.Steps.Count)
        {
            _currentStepIdx++;
            return;
        }

        _currentSetIdx++;
        _currentStepIdx = 0;

        if (_currentSetIdx >= run.Sets.Count)
            FinishRun();
    }

    private void StepBack()
    {
        if (_currentStepIdx > 0)
        {
            _currentStepIdx--;
            return;
        }

        if (_currentSetIdx > 0)
        {
            _currentSetIdx--;
            _currentStepIdx = _run!.Sets[_currentSetIdx].Steps.Count - 1;
        }
    }

    private void FinishRun()
    {
        var run = _run!;
        run.FinishedUtc = DateTime.UtcNow;
        _state = State.Summary;
    }

    // ----- Summary state -----------------------------------------------

    private void DrawSummaryState()
    {
        var run = _run!;
        FormInputs.AddSectionHeader("Run summary");

        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        ImGui.TextWrapped($"Started {run.StartedUtc:HH:mm:ss} UTC, finished {run.FinishedUtc:HH:mm:ss} UTC " +
                          $"— {run.Sets.Count} set(s), {run.Results.Count} step(s).");
        ImGui.PopStyleColor();

        FormInputs.AddVerticalSpace(8);

        foreach (var set in run.Sets)
        {
            var pass = 0;
            var fail = 0;
            var other = 0;
            var skipped = 0;
            foreach (var r in run.Results)
            {
                if (r.SetId != set.Id) continue;
                switch (r.Outcome)
                {
                    case Outcome.Pass: pass++; break;
                    case Outcome.Fail: fail++; break;
                    case Outcome.Other: other++; break;
                    default: skipped++; break;
                }
            }

            ImGui.PushFont(Fonts.FontBold);
            ImGui.TextUnformatted(set.Title);
            ImGui.PopFont();
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
            ImGui.TextUnformatted($"  {pass} pass / {fail} fail / {other} other / {skipped} skipped");
            ImGui.PopStyleColor();

            foreach (var r in run.Results)
            {
                if (r.SetId != set.Id) continue;
                if (r.StepIndex >= set.Steps.Count) continue;

                var icon = r.Outcome switch
                              {
                                  Outcome.Pass => "✓",
                                  Outcome.Fail => "✗",
                                  Outcome.Other => "?",
                                  _ => "·",
                              };
                var color = r.Outcome switch
                               {
                                   Outcome.Pass => UiColors.StatusControlled,
                                   Outcome.Fail => UiColors.StatusWarning,
                                   Outcome.Other => UiColors.StatusAttention,
                                   _ => UiColors.TextMuted,
                               };
                ImGui.PushStyleColor(ImGuiCol.Text, color.Rgba);
                ImGui.TextUnformatted($"  {icon}");
                ImGui.PopStyleColor();
                ImGui.SameLine();
                ImGui.TextUnformatted(set.Steps[r.StepIndex].Title);
                if (!string.IsNullOrEmpty(r.Comment))
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
                    ImGui.TextWrapped($"      {r.Comment}");
                    ImGui.PopStyleColor();
                }
            }

            FormInputs.AddVerticalSpace(8);
        }

        ImGui.Separator();
        FormInputs.AddVerticalSpace(4);

        // Phase 1: export buttons stubbed. Phase 2 will fill these in.
        ImGui.BeginDisabled();
        ImGui.Button("Copy JSON");
        ImGui.SameLine();
        ImGui.Button("Copy Markdown");
        ImGui.SameLine();
        ImGui.Button("Open GitHub Issue");
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered())
            CustomComponents.TooltipForLastItem("Coming in Phase 2");

        ImGui.SameLine();
        if (ImGui.Button("New Run"))
        {
            _state = State.Pick;
            _run = null;
        }
    }

    // ----- Helpers ------------------------------------------------------

    private void StartRun()
    {
        var sets = _allSets!;
        var selected = new List<TestSet>();
        foreach (var s in sets)
        {
            if (_selectedSetIds.Contains(s.Id) && Matches(s, _filter, _selectedTags))
                selected.Add(s);
        }
        if (selected.Count == 0)
            return;

        _run = new RunReport
                   {
                       StartedUtc = DateTime.UtcNow,
                       FinishedUtc = DateTime.MinValue,
                       Sets = selected,
                       Results = new List<StepResult>(64),
                   };
        _currentSetIdx = 0;
        _currentStepIdx = 0;
        _cachedStepKey = (string.Empty, -1);
        _state = State.Run;
    }

    private StepResult GetOrCreateResult(string setId, int stepIdx)
    {
        var run = _run!;
        for (var i = 0; i < run.Results.Count; i++)
        {
            if (run.Results[i].SetId == setId && run.Results[i].StepIndex == stepIdx)
                return run.Results[i];
        }
        var created = new StepResult
                          {
                              SetId = setId,
                              StepIndex = stepIdx,
                          };
        run.Results.Add(created);
        return created;
    }

    private int ComputeGlobalStepIndex()
    {
        var run = _run!;
        var idx = 0;
        for (var i = 0; i < _currentSetIdx; i++)
            idx += run.Sets[i].Steps.Count;
        idx += _currentStepIdx;
        return idx;
    }

    private int ComputeTotalStepCount()
    {
        var run = _run!;
        var total = 0;
        foreach (var s in run.Sets)
            total += s.Steps.Count;
        return total;
    }

    private static bool Matches(TestSet set, string filter, IReadOnlySet<string> requiredTags)
    {
        foreach (var tag in requiredTags)
        {
            if (!set.Tags.Contains(tag))
                return false;
        }

        if (string.IsNullOrWhiteSpace(filter))
            return true;
        if (set.Title.Contains(filter, StringComparison.OrdinalIgnoreCase))
            return true;
        if (set.Scope.Contains(filter, StringComparison.OrdinalIgnoreCase))
            return true;
        if (set.Id.Contains(filter, StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    private static List<string> CollectAllTags(List<TestSet> sets)
    {
        var seen = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in sets)
            foreach (var t in s.Tags)
                seen.Add(t);
        return new List<string>(seen);
    }

    private void ReloadSets()
    {
        _testsDir = TestSetParser.ResolveTestsDirectory();
        _allSets = TestSetParser.LoadAll(_testsDir);
        _selectedSetIds.RemoveWhere(id => !ContainsId(_allSets, id));
    }

    private static bool ContainsId(List<TestSet> sets, string id)
    {
        foreach (var s in sets)
            if (s.Id == id) return true;
        return false;
    }

    private enum State
    {
        Pick,
        Run,
        Summary,
    }

    private State _state = State.Pick;

    private List<TestSet>? _allSets;
    private string _testsDir = string.Empty;

    private readonly HashSet<string> _selectedSetIds = new(StringComparer.OrdinalIgnoreCase);
    private string _filter = string.Empty;
    private readonly HashSet<string> _selectedTags = new(StringComparer.OrdinalIgnoreCase);

    private RunReport? _run;
    private int _currentSetIdx;
    private int _currentStepIdx;

    private readonly MarkdownView _contextMarkdown = new(new MarkdownView.Options());
    private readonly MarkdownView _actionMarkdown = new(new MarkdownView.Options());
    private readonly MarkdownView _expectedMarkdown = new(new MarkdownView.Options());
    private (string SetId, int StepIdx) _cachedStepKey = (string.Empty, -1);
    private string? _cachedContext;
    private string? _cachedAction;
    private string? _cachedExpected;

    private static readonly StringBuilder _scratch = new(128);
}
