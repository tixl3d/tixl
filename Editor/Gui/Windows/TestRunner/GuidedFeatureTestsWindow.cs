#nullable enable
using System.Text;
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.SystemUi;
using T3.Editor.Gui.Help;
using T3.Editor.Gui.Hub;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.Styling.Markdown;

namespace T3.Editor.Gui.Windows.TestRunner;

/// <summary>
/// In-editor manual test runner. Parses <c>.tests-manual/*.md</c>, walks through steps
/// recording Pass/Fail/Other + comment, summarizes in-memory.
/// State machine: Pick → Run → Summary → Pick.
/// </summary>
internal sealed class GuidedFeatureTestsWindow : Window
{
    internal GuidedFeatureTestsWindow()
    {
        Config.Title = "Guided Feature Tests";
        MenuTitle = "Guided Feature Tests...";
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
        var displayDir = ShortenTestsDir(_testsDir);
        var subtitle = $"Loaded {sets.Count} test set{(sets.Count == 1 ? "" : "s")} from {displayDir}";

        ContentPanel.Begin("Guided Feature Tests", subtitle, drawTools: DrawPickHeaderTools);

        DrawFilterRow(sets);
        FormInputs.AddVerticalSpace(6);

        // Reserve room at the bottom for the footer so the list scrolls
        // independently while the action row stays pinned.
        var scale = T3Ui.UiScaleFactor;
        var footerH = ImGui.GetFrameHeight() + 16 * scale;
        ImGui.BeginChild("##setlist", new Vector2(0, -footerH),
                         ImGuiChildFlags.None, ImGuiWindowFlags.NoBackground);
        {
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, 4 * scale));

            var visibleCount = 0;
            foreach (var set in sets)
            {
                if (!Matches(set, _filter, _selectedTags))
                    continue;

                visibleCount++;
                DrawSetRow(set);
            }

            ImGui.PopStyleVar();

            if (visibleCount == 0)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
                ImGui.TextWrapped(sets.Count == 0
                                      ? $"No `.md` test sets found in {_testsDir}"
                                      : "No sets match the current filter.");
                ImGui.PopStyleColor();
            }
        }
        ImGui.EndChild();

        DrawPickFooter(sets);

        ContentPanel.End();
    }

    private void DrawPickHeaderTools()
    {
        var iconSize = new Vector2(ImGui.GetFrameHeight(), ImGui.GetFrameHeight());
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        CustomComponents.RightAlign(iconSize.X * 3 + spacing * 2, sameLine: false);

        DocumentationButton.Draw("GuidedFeatureTests", DocumentationWikiUrl, iconSize);

        ImGui.SameLine();
        // BeginDisabled doesn't dim a transparent icon button (the glyph is drawn via the draw list,
        // bypassing the disabled alpha), so use the Disabled button state for the dimmed look and just
        // ignore the click when nothing is selected.
        var anySelected = _selectedSetIds.Count > 0;
        var openState = anySelected ? CustomComponents.ButtonStates.Emphasized : CustomComponents.ButtonStates.Disabled;
        if (CustomComponents.TransparentIconButton(Icon.OpenExternally, iconSize, openState) && anySelected)
            OpenSelectedSourceFiles();
        if (ImGui.IsItemHovered())
            CustomComponents.TooltipForLastItem("Open source files for the selected tests");

        ImGui.SameLine();
        if (CustomComponents.TransparentIconButton(Icon.Refresh, iconSize))
            ReloadSets();
        if (ImGui.IsItemHovered())
            CustomComponents.TooltipForLastItem("Reload test sets from disk");
    }

    private void OpenSelectedSourceFiles()
    {
        if (_allSets == null)
            return;

        foreach (var set in _allSets)
        {
            if (_selectedSetIds.Contains(set.Id) && !string.IsNullOrEmpty(set.SourcePath))
                CoreUi.Instance.OpenWithDefaultApplication(set.SourcePath);
        }
    }

    private const string DocumentationWikiUrl = "https://github.com/tixl3d/tixl/wiki/dev.GuidedFeatureTests";

    private void DrawFilterRow(List<TestSet> sets)
    {
        var scale = T3Ui.UiScaleFactor;

        ImGui.SetNextItemWidth(220 * scale);
        ImGui.InputTextWithHint("##filter", "Filter…", ref _filter, 256);

        ImGui.SameLine();
        ImGui.SetNextItemWidth(150 * scale);
        var sortIdx = (int)_sortMode;
        if (ImGui.Combo("##sort", ref sortIdx, "Recently added\0Alphabetical\0By scope\0"))
        {
            _sortMode = (TestSetSort)sortIdx;
            TestSetParser.Sort(sets, _sortMode);
        }

        var allTags = CollectAllTags(sets);
        if (allTags.Count == 0)
            return;

        // Tags on their own row (left-aligned) so they don't overlap the filter/sort controls.
        for (var i = 0; i < allTags.Count; i++)
        {
            if (i > 0)
                ImGui.SameLine();
            var tag = allTags[i];
            var active = _selectedTags.Contains(tag);
            if (CustomComponents.TagFilterToggle(tag, active))
            {
                if (active) _selectedTags.Remove(tag);
                else _selectedTags.Add(tag);
            }
        }
    }

    private void DrawPickFooter(List<TestSet> sets)
    {
        var scale = T3Ui.UiScaleFactor;

        if (ImGui.Button("Select All"))
        {
            foreach (var s in sets)
                if (Matches(s, _filter, _selectedTags))
                    _selectedSetIds.Add(s.Id);
        }
        ImGui.SameLine();

        // Snapshot the disabled state so the matching pop runs even when the
        // click clears the set in-between (otherwise EndDisabled fires without
        // a matching Begin and ImGui's disabled stack underflows for one frame).
        var clearDisabled = _selectedSetIds.Count == 0;
        if (clearDisabled)
            ImGui.BeginDisabled();
        if (ImGui.Button("Clear"))
            _selectedSetIds.Clear();
        if (clearDisabled)
            ImGui.EndDisabled();

        // Start CTA right-aligned. Style follows the user's design proposal:
        // bright filled CTA when something is selected, dimmed otherwise.
        var startSize = CustomComponents.GetCtaButtonSize("Start");
        ImGui.SameLine();
        CustomComponents.RightAlign(startSize.X);

        var anySelected = _selectedSetIds.Count > 0;
        if (!anySelected)
            ImGui.BeginDisabled();

        var startBg = anySelected 
            ? Color.Mix( UiColors.StatusActivated, UiColors.BackgroundFull, 0.2f) 
            : UiColors.BackgroundButton;
        var startText = anySelected ? UiColors.ForegroundFull : UiColors.TextMuted;
        if (CustomComponents.DrawCtaButton("Start", Icon.None, startText, startBg, Color.Transparent))
            StartRun();

        if (!anySelected)
            ImGui.EndDisabled();
    }

    private void DrawSetRow(TestSet set)
    {
        var result = TestSetRow.Draw(set, _selectedSetIds.Contains(set.Id), height: 50);

        if (result.Clicked)
        {
            if (!_selectedSetIds.Remove(set.Id))
                _selectedSetIds.Add(set.Id);
        }

        // The shared row already shows a per-tick tooltip; otherwise show this set's intro markdown.
        if (result.Hovered && !result.TickHovered)
            DrawIntroTooltip(set);
    }

    private void DrawIntroTooltip(TestSet set)
    {
        if (string.IsNullOrWhiteSpace(set.Intro) && set.ParseWarnings.Count == 0)
            return;

        var scale = T3Ui.UiScaleFactor;

        // Pin the tooltip width BEFORE BeginTooltip so frame 1 already has the right ContentRegionAvail
        // (otherwise the auto-resize window flashes at viewport width on its first appearance).
        var tooltipWidth = 420 * scale;
        ImGui.SetNextWindowSizeConstraints(new Vector2(tooltipWidth, 0), new Vector2(tooltipWidth, float.MaxValue));

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10, 8) * scale);
        ImGui.BeginTooltip();

        if (!string.IsNullOrWhiteSpace(set.Intro))
        {
            // Lazy-create so hot-reload that adds new fields doesn't NRE until the next editor restart.
            _introMarkdown ??= new MarkdownView(new MarkdownView.Options());
            // Op/topic refs suppress their own tooltip — the intro already renders inside one.
            _introMarkdown.Draw(set.Intro,
                                onOperatorRef: static op => MarkdownOperatorLinks.HandleOperatorRef(op, suppressTooltip: true),
                                operatorColor: MarkdownOperatorLinks.GetOperatorColor);
        }

        if (set.ParseWarnings.Count > 0)
        {
            if (!string.IsNullOrWhiteSpace(set.Intro))
                FormInputs.AddVerticalSpace(6);
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.StatusWarning.Rgba);
            ImGui.PushTextWrapPos(tooltipWidth);
            foreach (var w in set.ParseWarnings)
                ImGui.TextWrapped(w);
            ImGui.PopTextWrapPos();
            ImGui.PopStyleColor();
        }

        ImGui.EndTooltip();
        ImGui.PopStyleVar();
    }

    private static string ShortenTestsDir(string fullPath)
    {
        if (string.IsNullOrEmpty(fullPath))
            return fullPath;
        // Show the .tests-manual folder name and one level above for context,
        // not the full Windows path.
        var name = System.IO.Path.GetFileName(fullPath);
        return string.IsNullOrEmpty(name) ? fullPath : name;
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

        var title = set.Title;
        var subtitle = $"Step {globalIdx + 1}/{globalCount} — {step.Title}";

        ContentPanel.Begin(title, subtitle, drawTools: DrawRunHeaderTools);

        // The Abandon button inside DrawRunHeaderTools may have nulled _run;
        // stop drawing the rest of this frame to avoid an NRE in the side panel.
        if (_run == null || _state != State.Run)
        {
            ContentPanel.End();
            return;
        }

        // Two-column layout: step content (left) + comment + outcome (right).
        var scale = T3Ui.UiScaleFactor;
        var gutter = 30f * scale;
        var avail = ImGui.GetContentRegionAvail();
        var rightWidth = MathF.Max(280 * scale, avail.X * 0.34f);
        var leftWidth = MathF.Max(280 * scale, avail.X - rightWidth - gutter);

        // The nav buttons in drawTools may have changed _currentSetIdx /
        // _currentStepIdx. Re-resolve set and step now so the body cache is
        // keyed and built from the CURRENT state, not the pre-click snapshot.
        set = run.Sets[_currentSetIdx];
        if (set.Steps.Count == 0)
        {
            ContentPanel.End();
            return;
        }
        step = set.Steps[_currentStepIdx];

        ImGui.BeginChild("##step-content", new Vector2(leftWidth, -1),
                         ImGuiChildFlags.None, ImGuiWindowFlags.NoBackground);
        DrawStepBody(set, step);
        ImGui.EndChild();

        ImGui.SameLine(0, gutter);
        ImGui.BeginChild("##step-side", new Vector2(rightWidth, -1),
                         ImGuiChildFlags.None, ImGuiWindowFlags.NoBackground);
        DrawStepSide(set, step);
        ImGui.EndChild();

        ContentPanel.End();
    }

    private void DrawRunHeaderTools()
    {
        var scale = T3Ui.UiScaleFactor;
        var iconSize = new Vector2(ImGui.GetFrameHeight(), ImGui.GetFrameHeight());
        // 4 icons + 1 group gap + 1 group gap.
        var toolsWidth = iconSize.X * 4 + 16 * scale;
        CustomComponents.RightAlign(toolsWidth, sameLine: false);

        // Edit this test set's source markdown — handy when the wording on a
        // step is awkward and you want to fix it in place.
        if (CustomComponents.TransparentIconButton(Icon.OpenExternally, iconSize))
            OpenCurrentSetSource();
        if (ImGui.IsItemHovered())
        {
            var set = _run?.Sets[_currentSetIdx];
            CustomComponents.TooltipForLastItem(
                "Open this test set's markdown source in the default editor",
                set?.SourcePath ?? string.Empty);
        }

        ImGui.SameLine(0, 8 * scale);
        if (CustomComponents.TransparentIconButton(Icon.ArrowLeft, iconSize))
            StepBack();
        if (ImGui.IsItemHovered())
            CustomComponents.TooltipForLastItem("Previous step");

        ImGui.SameLine(0, 0);
        if (CustomComponents.TransparentIconButton(Icon.ArrowRight, iconSize))
            AdvanceStep();
        if (ImGui.IsItemHovered())
            CustomComponents.TooltipForLastItem("Next step");

        ImGui.SameLine(0, 8 * scale);
        if (CustomComponents.TransparentIconButton(Icon.ChevronRight, iconSize))
            SkipToNextSet();
        if (ImGui.IsItemHovered())
            CustomComponents.TooltipForLastItem("Skip to the next set\n(ends the run if this is the last set)");
    }

    private void OpenCurrentSetSource()
    {
        if (_run == null || _currentSetIdx >= _run.Sets.Count)
            return;
        var path = _run.Sets[_currentSetIdx].SourcePath;
        if (string.IsNullOrEmpty(path))
            return;
        CoreUi.Instance.OpenWithDefaultApplication(path);
    }

    private void SkipToNextSet()
    {
        var run = _run!;
        _currentSetIdx++;
        _currentStepIdx = 0;
        if (_currentSetIdx >= run.Sets.Count)
            FinishRun();
    }

    private void DrawStepBody(TestSet set, TestStep step)
    {
        // Two markdown blocks per step (Context folds into Action). Caches
        // invalidate on step transition; subsequent frames are layout-free.
        if (_cachedStepKey != (set.Id, _currentStepIdx))
        {
            _cachedAction = BuildActionMarkdown(step);
            _cachedExpected = string.IsNullOrEmpty(step.ExpectedMarkdown) ? null : step.ExpectedMarkdown;
            _cachedStepKey = (set.Id, _currentStepIdx);
        }

        if (_cachedAction != null)
        {
            DrawSectionLabel("Please do the following");
            _actionMarkdown.Draw(_cachedAction,
                                 onOperatorRef: static op => MarkdownOperatorLinks.HandleOperatorRef(op),
                                 operatorColor: MarkdownOperatorLinks.GetOperatorColor);
        }

        if (_cachedExpected != null)
        {
            DrawSectionLabel("Expected Results");
            _expectedMarkdown.Draw(_cachedExpected,
                                   onOperatorRef: static op => MarkdownOperatorLinks.HandleOperatorRef(op),
                                   operatorColor: MarkdownOperatorLinks.GetOperatorColor);
        }
    }

    private static void DrawSectionLabel(string label)
    {
        FormInputs.AddVerticalSpace(10);
        CustomComponents.StylizedText(label, Fonts.FontSmall, UiColors.TextMuted);
    }

    private static string? BuildActionMarkdown(TestStep step)
    {
        var hasContext = !string.IsNullOrWhiteSpace(step.Context);
        var hasAction = !string.IsNullOrWhiteSpace(step.ActionMarkdown);
        if (!hasContext && !hasAction)
            return null;
        if (!hasContext)
            return step.ActionMarkdown;
        if (!hasAction)
            return step.Context;
        return step.Context + "\n\n" + step.ActionMarkdown;
    }

    private void DrawStepSide(TestSet set, TestStep step)
    {
        var result = GetOrCreateResult(set.Id, _currentStepIdx);

        CustomComponents.StylizedText("Actual result", Fonts.FontSmall, UiColors.TextMuted);

        var comment = result.Comment;
        var inputPos = ImGui.GetCursorScreenPos();
        if (ImGui.InputTextMultiline("##comment", ref comment, 1024,
                                      new Vector2(-1, 160 * T3Ui.UiScaleFactor)))
        {
            result.Comment = comment;
        }

        // Manual placeholder — ImGui has no InputTextMultilineWithHint.
        if (string.IsNullOrEmpty(result.Comment) && !ImGui.IsItemActive())
        {
            var dl = ImGui.GetWindowDrawList();
            var color = ImGui.GetColorU32(UiColors.TextMuted.Fade(0.6f).Rgba);
            var pad = ImGui.GetStyle().FramePadding;
            dl.AddText(inputPos + pad, color, "Add comment…");
        }

        FormInputs.AddVerticalSpace(10);

        // Outcome buttons via the project's standard CTA button helper.
        if (CustomComponents.DrawCtaButton("Success", Icon.None,
                                            UiColors.ForegroundFull,
                                            UiColors.StatusControlled,
                                            Color.Transparent))
            RecordOutcomeAndAdvance(result, Outcome.Pass);

        ImGui.SameLine();
        if (CustomComponents.DrawCtaButton("Fail", Icon.None,
                                            UiColors.ForegroundFull,
                                            UiColors.StatusWarning,
                                            Color.Transparent))
            RecordOutcomeAndAdvance(result, Outcome.Fail);

        ImGui.SameLine();
        var otherEnabled = !string.IsNullOrWhiteSpace(result.Comment);
        if (!otherEnabled)
            ImGui.BeginDisabled();
        if (CustomComponents.DrawCtaButton("Other…", Icon.None,
                                            CustomComponents.ButtonStates.Default))
            RecordOutcomeAndAdvance(result, Outcome.Other);
        if (!otherEnabled)
            ImGui.EndDisabled();
        if (!otherEnabled && ImGui.IsItemHovered())
            CustomComponents.TooltipForLastItem("Add a comment first");
    }

    private static void DrawOutcomeBadge(Outcome outcome)
    {
        var (icon, color) = outcome switch
                              {
                                  Outcome.Pass    => (Icon.Checkmark, UiColors.StatusControlled),
                                  Outcome.Fail    => (Icon.Error,     UiColors.StatusWarning),
                                  Outcome.Other   => (Icon.Warning,   UiColors.StatusAttention),
                                  Outcome.Skipped => (Icon.Hidden,    UiColors.TextMuted),
                                  _               => (Icon.Hidden,    UiColors.TextMuted),
                              };
        icon.DrawAtCursor(color);
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
        TestRunExport.AutoSave(run);
        TestRunResults.Invalidate(); // pick up this run's results for the checkmarks / status bars
        _state = State.Summary;
    }

    // ----- Summary state -----------------------------------------------

    private void DrawSummaryState()
    {
        var run = _run!;
        var subtitle = $"Started {run.StartedUtc:HH:mm:ss} UTC, finished {run.FinishedUtc:HH:mm:ss} UTC " +
                       $"— {run.Sets.Count} set(s), {run.Results.Count} step(s).";

        ContentPanel.Begin("Summary", subtitle);

        var scale = T3Ui.UiScaleFactor;
        var footerH = ImGui.GetFrameHeight() + 16 * scale;

        DrawBlackSeparator();

        ImGui.BeginChild("##summary-scroll", new Vector2(0, -footerH),
                         ImGuiChildFlags.None, ImGuiWindowFlags.NoBackground);
        DrawSummaryScrollContent(run);
        ImGui.EndChild();

        DrawBlackSeparator();

        DrawSummaryToolbar(run);

        ContentPanel.End();
    }

    private static void DrawSummaryScrollContent(RunReport run)
    {
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

            CustomComponents.StylizedText(set.Title, Fonts.FontBold, UiColors.Text);
            CustomComponents.StylizedText($"  {pass} pass / {fail} fail / {other} other / {skipped} skipped",
                                           Fonts.FontNormal, UiColors.TextMuted);

            foreach (var r in run.Results)
            {
                if (r.SetId != set.Id) continue;
                if (r.StepIndex >= set.Steps.Count) continue;

                DrawOutcomeBadge(r.Outcome);
                ImGui.SameLine();
                ImGui.TextUnformatted(set.Steps[r.StepIndex].Title);
                if (!string.IsNullOrEmpty(r.Comment))
                {
                    CustomComponents.StylizedText($"      {r.Comment}",
                                                   Fonts.FontNormal, UiColors.TextMuted);
                }
            }

            FormInputs.AddVerticalSpace(8);
        }
    }

    private void DrawSummaryToolbar(RunReport run)
    {
        // Copy Markdown is the one export wired in Phase 1; the others stay
        // visibly disabled with a tooltip for later phases.
        if (ImGui.Button("Copy Markdown"))
        {
            ImGui.SetClipboardText(BuildMarkdownReport(run));
        }

        ImGui.SameLine();
        if (ImGui.Button("Copy JSON"))
        {
            ImGui.SetClipboardText(TestRunExport.BuildJson(run));
        }

        ImGui.SameLine();
        if (ImGui.Button("Open Saved Runs"))
        {
            CoreUi.Instance.OpenWithDefaultApplication(TestRunExport.RunsDirectory);
        }

        if (ImGui.IsItemHovered())
            CustomComponents.TooltipForLastItem("Each finished run is auto-saved here as JSON.");

        ImGui.SameLine();
        ImGui.BeginDisabled();
        ImGui.Button("Open GitHub Issue");
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered())
            CustomComponents.TooltipForLastItem("Coming in a later phase");

        // Right-align "New Run".
        var newRunSize = ImGui.CalcTextSize("New Run") + ImGui.GetStyle().FramePadding * 2;
        ImGui.SameLine();
        CustomComponents.RightAlign(newRunSize.X);
        if (ImGui.Button("New Run"))
        {
            _state = State.Pick;
            _run = null;
        }
    }

    private static void DrawBlackSeparator()
    {
        ImGui.PushStyleColor(ImGuiCol.Separator, UiColors.BackgroundFull.Rgba);
        ImGui.Separator();
        ImGui.PopStyleColor();
    }

    private static string BuildMarkdownReport(RunReport run)
    {
        var sb = new StringBuilder();
        sb.Append("# Guided Feature Tests run\n\n");
        sb.Append($"Started {run.StartedUtc:yyyy-MM-dd HH:mm:ss} UTC  \n");
        sb.Append($"Finished {run.FinishedUtc:yyyy-MM-dd HH:mm:ss} UTC  \n");
        sb.Append($"Sets: {run.Sets.Count}, steps: {run.Results.Count}\n\n");

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

            sb.Append("## ").Append(set.Title)
              .Append(" — ").Append(pass).Append(" pass")
              .Append(" / ").Append(fail).Append(" fail")
              .Append(" / ").Append(other).Append(" other")
              .Append(" / ").Append(skipped).Append(" skipped\n");

            foreach (var r in run.Results)
            {
                if (r.SetId != set.Id) continue;
                if (r.StepIndex >= set.Steps.Count) continue;

                var glyph = r.Outcome switch
                              {
                                  Outcome.Pass    => "✓",
                                  Outcome.Fail    => "✗",
                                  Outcome.Other   => "?",
                                  _               => "·",
                              };
                sb.Append("- ").Append(glyph).Append(' ')
                  .Append(set.Steps[r.StepIndex].Title).Append('\n');

                // Keep each comment line as its own nested list item so multi-line
                // notes stay readable instead of collapsing onto the title line.
                if (!string.IsNullOrWhiteSpace(r.Comment))
                {
                    foreach (var commentLine in r.Comment.Split('\n'))
                    {
                        var trimmed = commentLine.Trim();
                        if (trimmed.Length == 0)
                            continue;

                        sb.Append("    - ").Append(trimmed).Append('\n');
                    }
                }
            }
            sb.Append('\n');
        }

        return sb.ToString();
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
        TestSetParser.Sort(_allSets, _sortMode);
        _selectedSetIds.RemoveWhere(id => !ContainsId(_allSets, id));
    }

    /// <summary>
    /// Opens the runner and starts a single-set run for <paramref name="setId"/>. Used by the welcome
    /// window's "Start Test" action. Falls back to the Pick state if the set can't be found.
    /// </summary>
    internal void StartSet(string setId)
    {
        Config.Visible = true;

        if (_allSets == null)
            ReloadSets();

        TestSet? target = null;
        foreach (var s in _allSets!)
        {
            if (s.Id == setId)
            {
                target = s;
                break;
            }
        }

        if (target == null)
        {
            _state = State.Pick;
            return;
        }

        _run = new RunReport
                   {
                       StartedUtc = DateTime.UtcNow,
                       FinishedUtc = DateTime.MinValue,
                       Sets = new List<TestSet> { target },
                       Results = new List<StepResult>(32),
                   };
        _currentSetIdx = 0;
        _currentStepIdx = 0;
        _cachedStepKey = (string.Empty, -1);
        _state = State.Run;
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
    private TestSetSort _sortMode = TestSetSort.RecentlyAdded;
    private readonly HashSet<string> _selectedTags = new(StringComparer.OrdinalIgnoreCase);

    private RunReport? _run;
    private int _currentSetIdx;
    private int _currentStepIdx;

    private readonly MarkdownView _actionMarkdown = new(new MarkdownView.Options());
    private readonly MarkdownView _expectedMarkdown = new(new MarkdownView.Options());
    private MarkdownView? _introMarkdown;
    private (string SetId, int StepIdx) _cachedStepKey = (string.Empty, -1);
    private string? _cachedAction;
    private string? _cachedExpected;

    private static readonly StringBuilder _scratch = new(128);
}
