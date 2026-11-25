#nullable enable
using System.Text;
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.SystemUi;
using T3.Core.Utils;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.Windows.Analyze.Benchmark;

/// <summary>
/// Runs the benchmark suite and shows one graph per test: duration over load on log axes, with the duration at the
/// test's authored Reference as its headline number. Saved runs from other machines, OSes or releases can be
/// overlaid for comparison.
/// </summary>
internal sealed class BenchmarkWindow : Window
{
    public BenchmarkWindow()
    {
        Config.Title = "Benchmark";
        MenuTitle = "Benchmark";
        WindowPaddingOverride = new Vector2(7);
    }

    internal override List<Window> GetInstances() => [];

    protected override void DrawContent()
    {
        ReloadSavedRunsIfNeeded();
        var primary = BenchmarkRunner.LatestRun ?? (_savedRuns.Count > 0 ? _savedRuns[0] : null);

        DrawToolbar(primary);
        if (primary == null)
        {
            CustomComponents.HelpText("""
                                      Measures how the duration grows with load for CPU, compute and fill-rate work,
                                      and where a typical amount (the Reference) lands on this machine. A run takes
                                      about a minute; avoid using the editor meanwhile.
                                      """);
            return;
        }

        FormInputs.AddVerticalSpace(5);
        DrawGraphs(primary);
        FormInputs.AddVerticalSpace(5);
        DrawSystemInfo(primary);
        FormInputs.AddVerticalSpace(5);
        DrawHistory(primary);
        DrawCompareSelection(primary);
    }

    private void DrawToolbar(BenchmarkRun? primary)
    {
        if (BenchmarkRunner.IsRunning)
        {
            if (ImGui.Button("Cancel"))
                BenchmarkRunner.Cancel();

            ImGui.SameLine();
            ImGui.TextUnformatted(BenchmarkRunner.Progress);
            return;
        }

        if (ImGui.Button("Run benchmark"))
            BenchmarkRunner.Start();

        CustomComponents.TooltipForLastItem("Sweeps every test from small to large load, back down and up again (about a minute and a half).",
                                            "VSync is turned off during the run and restored afterwards.");

        if (primary != null)
        {
            ImGui.SameLine();
            if (ImGui.Button("Copy summary"))
                ImGui.SetClipboardText(BuildSummary(primary));

            CustomComponents.TooltipForLastItem("Copies a Markdown table with the duration at the Reference per test.");
        }

        ImGui.SameLine();
        if (ImGui.Button("Open folder"))
        {
            System.IO.Directory.CreateDirectory(BenchmarkRun.FolderPath);
            CoreUi.Instance.OpenWithDefaultApplication(BenchmarkRun.FolderPath);
        }

        CustomComponents.TooltipForLastItem("Results are saved here. Copy results from other machines into it to compare them.");

        if (!string.IsNullOrEmpty(BenchmarkRunner.Progress))
        {
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
            ImGui.TextUnformatted(BenchmarkRunner.Progress);
            ImGui.PopStyleColor();
            CustomComponents.TooltipForLastItem(EscapeFormat(BenchmarkRun.FolderPath));
        }
    }

    private static void DrawSystemInfo(BenchmarkRun run)
    {
        var system = run.System;
        ImGui.PushFont(Fonts.FontSmall);
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        TextWrappedUnformatted($"{run.DisplayName} · {system.Backend} · {system.Driver}");
        TextWrappedUnformatted($"{system.Os} · TiXL {system.TixlVersion} {system.BuildConfiguration} {system.Commit} · power: {system.PowerSource}");
        if (!string.IsNullOrEmpty(run.Note))
            TextWrappedUnformatted(run.Note);
        ImGui.PopStyleColor();

        if (system.IsDebugBuild || system.ValidationLayer)
        {
            var reason = system.IsDebugBuild && system.ValidationLayer
                             ? "Debug build with validation layer"
                             : system.IsDebugBuild
                                 ? "Debug build"
                                 : "Validation layer on";
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.StatusWarning.Rgba);
            TextWrappedUnformatted($"{reason}: CPU times are only comparable with runs of the same configuration.");
            ImGui.PopStyleColor();
        }

        ImGui.PopFont();
    }

    /// <summary>
    /// The runs of this machine and configuration, newest first, with the duration at each Reference and its change
    /// against the run below it, to spot regressions between commits.
    /// </summary>
    private static void DrawHistory(BenchmarkRun primary)
    {
        UpdateHistoryRows(primary);
        if (_historyRows.Count < 2)
            return;

        if (!ImGui.CollapsingHeader($"History ({_historyRows.Count} runs of {primary.Label} on this GPU, power: {primary.System.PowerSource})###benchmarkHistory",
                                    ImGuiTreeNodeFlags.DefaultOpen))
            return;

        var tests = BenchmarkSuite.Tests;
        if (!ImGui.BeginTable("##benchmarkHistory", 3 + tests.Length, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit))
            return;

        ImGui.TableSetupColumn("Date");
        ImGui.TableSetupColumn("Commit");
        foreach (var test in tests)
        {
            ImGui.TableSetupColumn(test.Category);
        }

        ImGui.TableSetupColumn("Note", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        foreach (var row in _historyRows)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(row.Date);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(row.Run.System.Commit);

            for (var testIndex = 0; testIndex < tests.Length; testIndex++)
            {
                ImGui.TableNextColumn();
                var cell = row.Cells[testIndex];
                ImGui.PushStyleColor(ImGuiCol.Text, ColorForVerdict(cell.Verdict).Rgba);
                ImGui.TextUnformatted(cell.Text);
                ImGui.PopStyleColor();
                if (cell.TestChanged)
                    CustomComponents.TooltipForLastItem("The test or one of its operators changed since the run below.");
            }

            ImGui.TableNextColumn();
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
            ImGui.TextUnformatted(row.Run.Note);
            ImGui.PopStyleColor();
        }

        ImGui.EndTable();
        CustomComponents.HelpText(EscapeFormat($"Changes above {BenchmarkHistory.MinRelativeChange * 100:0} % are coloured: green faster, red slower than the run below. * marks a changed test."));
    }

    private static void UpdateHistoryRows(BenchmarkRun primary)
    {
        if (_historyPrimary == primary && _historyRunsVersion == _loadedRunsVersion && _historyRowsAreFresh)
            return;

        _historyPrimary = primary;
        _historyRunsVersion = _loadedRunsVersion;
        _historyRowsAreFresh = true;
        _historyRows.Clear();

        var runs = BenchmarkHistory.GetSameSetupRuns(primary, _savedRuns);
        for (var index = 0; index < runs.Count; index++)
        {
            var run = runs[index];
            var older = index + 1 < runs.Count ? runs[index + 1] : null;
            var comparisons = older != null ? BenchmarkHistory.Compare(older, run) : null;
            var cells = new HistoryCell[BenchmarkSuite.Tests.Length];
            for (var testIndex = 0; testIndex < cells.Length; testIndex++)
            {
                var test = BenchmarkSuite.Tests[testIndex];
                if (!BenchmarkHistory.TryGetReferenceMs(run, test, out var ms, out _))
                {
                    cells[testIndex] = new HistoryCell("–", BenchmarkHistory.Verdict.NotComparable, false);
                    continue;
                }

                var text = FormatMs(ms);
                var verdict = BenchmarkHistory.Verdict.NotComparable;
                var testChanged = false;
                if (comparisons != null)
                {
                    var comparison = comparisons[testIndex];
                    verdict = comparison.Verdict;
                    testChanged = comparison.TestChanged;
                    if (verdict is BenchmarkHistory.Verdict.Improved or BenchmarkHistory.Verdict.Regressed or BenchmarkHistory.Verdict.Unchanged)
                        text += $" ({comparison.ChangePercent:+0;-0;0} %)";
                }

                if (testChanged)
                    text += " *";

                cells[testIndex] = new HistoryCell(text, verdict, testChanged);
            }

            _historyRows.Add(new HistoryRow(run, run.StartedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), cells));
        }
    }

    private static Color ColorForVerdict(BenchmarkHistory.Verdict verdict)
    {
        return verdict switch
                   {
                       BenchmarkHistory.Verdict.Improved  => UiColors.StatusOkay,
                       BenchmarkHistory.Verdict.Regressed => UiColors.StatusWarning,
                       _                                  => UiColors.Text,
                   };
    }

    private readonly record struct HistoryCell(string Text, BenchmarkHistory.Verdict Verdict, bool TestChanged);

    private sealed record HistoryRow(BenchmarkRun Run, string Date, HistoryCell[] Cells);

    private void DrawCompareSelection(BenchmarkRun primary)
    {
        var others = 0;
        foreach (var run in _savedRuns)
        {
            if (run.FilePath != primary.FilePath)
                others++;
        }

        if (others == 0)
            return;

        if (!ImGui.CollapsingHeader($"Compare ({_comparedPaths.Count} of {others})"))
            return;

        // Same order as the overlays, so each checkbox shows its curve's colour.
        var colorIndex = 0;
        foreach (var run in _savedRuns)
        {
            if (run.FilePath == primary.FilePath)
                continue;

            var isCompared = _comparedPaths.Contains(run.FilePath);
            var color = isCompared ? GetCompareColor(colorIndex++) : UiColors.Text;
            ImGui.PushStyleColor(ImGuiCol.Text, color.Rgba);
            var changed = ImGui.Checkbox($"{run.DisplayName}  {run.System.Adapter}{DescribeDifference(primary, run)}##{run.FilePath}",
                                         ref isCompared);
            ImGui.PopStyleColor();

            if (!changed)
                continue;

            if (isCompared)
            {
                _comparedPaths.Add(run.FilePath);
            }
            else
            {
                _comparedPaths.Remove(run.FilePath);
            }
        }
    }

    private static string DescribeDifference(BenchmarkRun primary, BenchmarkRun other)
    {
        var notes = new List<string>();
        if (other.System.BuildConfiguration != primary.System.BuildConfiguration)
            notes.Add(other.System.BuildConfiguration);

        if (other.SuiteVersion != primary.SuiteVersion)
            notes.Add($"suite v{other.SuiteVersion}");

        foreach (var test in other.Tests)
        {
            if (primary.TryGetTest(test.Key, out var mine) && mine.Fingerprint != test.Fingerprint)
            {
                notes.Add("tests changed");
                break;
            }
        }

        return notes.Count == 0 ? string.Empty : $"  ({string.Join(", ", notes)})";
    }

    private void DrawGraphs(BenchmarkRun primary)
    {
        var scale = T3Ui.UiScaleFactor;
        var spacing = MathF.Round(GraphSpacing * scale);
        var available = ImGui.GetContentRegionAvail().X;
        var columns = Math.Clamp((int)((available + spacing) / (MinGraphWidth * scale + spacing)), 1, BenchmarkSuite.Tests.Length);
        var width = MathF.Floor((available - spacing * (columns - 1)) / columns);

        _comparedRuns.Clear();
        foreach (var run in _savedRuns)
        {
            if (run.FilePath != primary.FilePath && _comparedPaths.Contains(run.FilePath))
                _comparedRuns.Add(run);
        }

        UpdateHeaders(primary);
        var wrapHeaders = DoesAnyHeaderOverflow(width - 2 * MathF.Round(PanelPadding * scale));

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(spacing, spacing));
        for (var index = 0; index < BenchmarkSuite.Tests.Length; index++)
        {
            if (index % columns != 0)
                ImGui.SameLine();

            DrawTestGraph(index, primary, new Vector2(width, MathF.Round(GraphHeight * scale)), wrapHeaders);
        }

        ImGui.PopStyleVar();
    }

    /// <summary>
    /// True when a title, grade and duration don't fit on one line in some panel. All panels then move the duration
    /// to a second line, so their plots stay aligned.
    /// </summary>
    private static bool DoesAnyHeaderOverflow(float innerWidth)
    {
        var scale = T3Ui.UiScaleFactor;
        ImGui.PushFont(Fonts.FontLarge);
        var overflows = false;
        for (var index = 0; index < BenchmarkSuite.Tests.Length && !overflows; index++)
        {
            var header = _headers[index];
            var width = ImGui.CalcTextSize(BenchmarkSuite.Tests[index].Category).X + HeaderGap * scale
                        + ImGui.CalcTextSize(header.Detail).X;
            if (header.Grade.Length > 0)
                width += ImGui.CalcTextSize(header.Grade).X + GradeGap * scale;

            overflows = width > innerWidth;
        }

        ImGui.PopFont();
        return overflows;
    }

    private void DrawTestGraph(int testIndex, BenchmarkRun primary, Vector2 size, bool wrapHeader)
    {
        var test = BenchmarkSuite.Tests[testIndex];
        var header = _headers[testIndex];
        var scale = T3Ui.UiScaleFactor;
        var padding = MathF.Round(PanelPadding * scale);
        var drawList = ImGui.GetWindowDrawList();
        var cursor = ImGui.GetCursorScreenPos();
        var min = new Vector2(MathF.Floor(cursor.X), MathF.Floor(cursor.Y));
        ImGui.InvisibleButton(test.Key, size);
        var isHovered = ImGui.IsItemHovered();
        var max = min + size;

        drawList.AddRectFilled(min, max, UiColors.BackgroundFull.Fade(0.3f), 4 * scale);

        // Header: category, grade and the duration at the Reference
        var headerPos = min + new Vector2(padding, padding * 0.7f);
        ImGui.PushFont(Fonts.FontLarge);
        drawList.PushClipRect(min, max, true);
        var categorySize = ImGui.CalcTextSize(test.Category);
        drawList.AddText(headerPos, UiColors.ForegroundFull, test.Category);
        var x = headerPos.X + categorySize.X + HeaderGap * scale;
        if (header.Grade.Length > 0)
        {
            drawList.AddText(new Vector2(x, headerPos.Y), header.GradeColor, header.Grade);
            x += ImGui.CalcTextSize(header.Grade).X + GradeGap * scale;
        }

        if (!wrapHeader)
        {
            drawList.AddText(new Vector2(x, headerPos.Y), UiColors.TextMuted, header.Detail);
        }
        else
        {
            // On its own line the duration still has to be readable in narrow panels.
            var detailInLargeFontFits = ImGui.CalcTextSize(header.Detail).X <= size.X - 2 * padding;
            if (!detailInLargeFontFits)
                ImGui.PushFont(Fonts.FontNormal);

            var detailY = headerPos.Y + categorySize.Y + (detailInLargeFontFits ? 0 : (categorySize.Y - ImGui.GetTextLineHeight()) / 2);
            drawList.AddText(new Vector2(headerPos.X, detailY), UiColors.TextMuted, header.Detail);
            if (!detailInLargeFontFits)
                ImGui.PopFont();
        }

        drawList.PopClipRect();
        ImGui.PopFont();

        ImGui.PushFont(Fonts.FontSmall);
        var smallLineHeight = ImGui.GetTextLineHeight();
        var subtitleY = headerPos.Y + categorySize.Y * (wrapHeader ? 2 : 1);
        drawList.PushClipRect(min, new Vector2(max.X - padding, max.Y), true);
        drawList.AddText(new Vector2(headerPos.X, subtitleY), UiColors.TextMuted, test.Description);
        drawList.PopClipRect();

        var plotMin = new Vector2(min.X + padding + 36 * scale, subtitleY + smallLineHeight + 14 * scale);
        var plotMax = max - new Vector2(padding, padding + smallLineHeight);
        var plot = new ImRect(plotMin, plotMax);
        var minLoadLog = Math.Log10(test.MinLoad);
        var maxLoadLog = Math.Log10(test.MaxLoad);

        // Grid: duration lines and amount decades
        foreach (var (ms, label) in _durationLines)
        {
            var y = YForMs(plot, ms);
            var isBudgetLine = ms is 8 or 16;
            drawList.AddLine(new Vector2(plot.Min.X, y), new Vector2(plot.Max.X, y),
                             UiColors.ForegroundFull.Fade(isBudgetLine ? 0.18f : 0.07f));
            var labelSize = ImGui.CalcTextSize(label);
            drawList.AddText(new Vector2(plot.Min.X - labelSize.X - 4 * scale, y - labelSize.Y / 2), UiColors.TextMuted, label);
        }

        for (var decade = 1L; decade <= test.MaxLoad; decade *= 10)
        {
            if (decade < test.MinLoad)
                continue;

            var decadeX = XForLoad(plot, decade, minLoadLog, maxLoadLog);
            drawList.AddLine(new Vector2(decadeX, plot.Min.Y), new Vector2(decadeX, plot.Max.Y), UiColors.ForegroundFull.Fade(0.04f));
            var label = BenchmarkRunner.FormatLoad(decade);
            var labelSize = ImGui.CalcTextSize(label);
            drawList.AddText(new Vector2(decadeX - labelSize.X / 2, plot.Max.Y + 3 * scale), UiColors.TextMuted, label);
        }

        ImGui.PopFont();

        drawList.PushClipRect(plot.Min - Vector2.One * 4 * scale, plot.Max + Vector2.One * 4 * scale, true);

        // Other runs, faded
        for (var runIndex = 0; runIndex < _comparedRuns.Count; runIndex++)
        {
            if (!_comparedRuns[runIndex].TryGetTest(test.Key, out var otherResult))
                continue;

            otherResult.GetCurve(test.PlotsCpuTime, _curveScratch);
            DrawCurveLine(drawList, plot, test, _curveScratch, GetCompareColor(runIndex).Fade(0.6f), 1.5f * scale);
        }

        // This run: spread of the passes, mean line, unsteady steps
        primary.TryGetTest(test.Key, out var result);
        _curve.Clear();
        result?.GetCurve(test.PlotsCpuTime, _curve);
        DrawBand(drawList, plot, test, _curve);
        DrawCurveLine(drawList, plot, test, _curve, UiColors.ForegroundFull, 2 * scale);
        foreach (var point in _curve)
        {
            if (!point.IsUnsteady)
                continue;

            var position = new Vector2(XForLoad(plot, point.Load, minLoadLog, maxLoadLog), YForMs(plot, point.MeanMs));
            drawList.AddCircle(position, 3.5f * scale, UiColors.StatusWarning, 12, 1.5f * scale);
        }

        // Reference
        var referenceX = XForLoad(plot, test.Reference, minLoadLog, maxLoadLog);
        drawList.AddLine(new Vector2(referenceX, plot.Min.Y), new Vector2(referenceX, plot.Max.Y), UiColors.ForegroundFull.Fade(0.35f));
        if (BenchmarkTestResult.TryInterpolate(_curve, test.Reference, out var referenceMs))
            drawList.AddCircleFilled(new Vector2(referenceX, YForMs(plot, referenceMs)), 4 * scale, header.GradeColor);

        drawList.PopClipRect();

        if (!isHovered)
            return;

        _tooltipTestIndex = testIndex;
        _tooltipResult = result;
        if (ImGui.GetMousePos().Y < plot.Min.Y)
        {
            CustomComponents.TooltipForLastItem(DrawHeaderTooltip, useHoverDelay: false);
            return;
        }

        if (!TryFindNearestPoint(plot, minLoadLog, maxLoadLog, out var nearestLoad))
            return;

        var nearestX = XForLoad(plot, nearestLoad, minLoadLog, maxLoadLog);
        drawList.AddLine(new Vector2(nearestX, plot.Min.Y), new Vector2(nearestX, plot.Max.Y), UiColors.ForegroundFull.Fade(0.2f));
        _tooltipLoad = nearestLoad;
        CustomComponents.TooltipForLastItem(DrawPointTooltip, useHoverDelay: false);
    }

    private static bool TryFindNearestPoint(ImRect plot, double minLoadLog, double maxLoadLog, out int load)
    {
        var mouseX = ImGui.GetMousePos().X;
        load = 0;
        var nearestDistance = float.MaxValue;
        foreach (var candidate in _curve)
        {
            var distance = Math.Abs(XForLoad(plot, candidate.Load, minLoadLog, maxLoadLog) - mouseX);
            if (distance >= nearestDistance)
                continue;

            nearestDistance = distance;
            load = candidate.Load;
        }

        return nearestDistance < float.MaxValue;
    }

    /** What the test and its grade stand for. */
    private static void DrawHeaderTooltip()
    {
        var test = BenchmarkSuite.Tests[_tooltipTestIndex];
        ImGui.PushTextWrapPos(TooltipWrapWidth * T3Ui.UiScaleFactor);
        CustomComponents.StylizedText(test.Category, Fonts.FontLarge, UiColors.ForegroundFull);
        ImGui.TextUnformatted($"Reference: {test.ReferenceDescription}");
        var isDebugBuild = _headersRun?.System.IsDebugBuild == true;
        var gradeScale = test.GetGradeScale(isDebugBuild);
        ImGui.TextUnformatted($"Grades go from A+ (up to {FormatMs(test.GradeBestMs * gradeScale)}) to F (from {FormatMs(test.GradeWorstMs * gradeScale)}); "
                               + $"each step from A to D- is {(test.GradeStepFactor - 1) * 100:0} % slower than the one before.");
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        ImGui.TextUnformatted(test.PlotsCpuTime
                                   ? "Plotted: main-thread time of the evaluation."
                                   : "Plotted: time until the GPU has finished the work.");
        if (gradeScale > 1)
            ImGui.TextUnformatted($"Debug builds evaluate about {gradeScale:0.#}× slower, so the grade range is stretched by that factor.");

        if (_tooltipResult != null && !string.IsNullOrEmpty(_tooltipResult.StopReason))
            ImGui.TextUnformatted($"Stopped: {_tooltipResult.StopReason}");

        ImGui.PopStyleColor();
        ImGui.PopTextWrapPos();
    }

    /** The fastest and slowest pass at the hovered amount. */
    private static void DrawPointTooltip()
    {
        var test = BenchmarkSuite.Tests[_tooltipTestIndex];
        CustomComponents.StylizedText(test.Category, Fonts.FontLarge, UiColors.ForegroundFull);
        ImGui.TextUnformatted($"{BenchmarkRunner.FormatLoad(_tooltipLoad)} {test.UnitLabel}");
        if (_tooltipResult == null)
            return;

        BenchmarkStep? fastest = null;
        BenchmarkStep? slowest = null;
        foreach (var step in _tooltipResult.Steps)
        {
            if (step.Load != _tooltipLoad)
                continue;

            var ms = PlottedMs(test, step);
            if (fastest == null || ms < PlottedMs(test, fastest))
                fastest = step;

            if (slowest == null || ms > PlottedMs(test, slowest))
                slowest = step;
        }

        if (fastest == null || slowest == null)
            return;

        if (fastest == slowest)
        {
            DrawStepLine(string.Empty, test, fastest);
            return;
        }

        DrawStepLine("Min: ", test, fastest);
        DrawStepLine("Max: ", test, slowest);
    }

    private static void DrawStepLine(string label, BenchmarkTest test, BenchmarkStep step)
    {
        CustomComponents.StylizedText(label + FormatMs(PlottedMs(test, step)), Fonts.FontBold, UiColors.ForegroundFull);
        ImGui.SameLine(0, 4 * T3Ui.UiScaleFactor);
        var gpu = double.IsNaN(step.GpuMs) ? "n/a" : FormatMs(step.GpuMs);
        ImGui.TextUnformatted($"({FormatMs(step.CpuMs)} CPU, {gpu} GPU)");
    }

    private static double PlottedMs(BenchmarkTest test, BenchmarkStep step) => test.PlotsCpuTime ? step.CpuMs : step.StepMs;

    /// <summary>The header text only changes when a step is added or another run is shown, so it is built then.</summary>
    private static void UpdateHeaders(BenchmarkRun primary)
    {
        var stepCount = 0;
        foreach (var result in primary.Tests)
        {
            stepCount += result.Steps.Count;
        }

        if (primary == _headersRun && stepCount == _headersStepCount)
            return;

        _headersRun = primary;
        _headersStepCount = stepCount;
        for (var index = 0; index < BenchmarkSuite.Tests.Length; index++)
        {
            var test = BenchmarkSuite.Tests[index];
            var header = new TestHeader(string.Empty, UiColors.TextMuted, string.Empty);
            if (primary.TryGetTest(test.Key, out var result))
            {
                result.GetCurve(test.PlotsCpuTime, _curveScratch);
                var reference = BenchmarkRunner.FormatLoad(test.Reference);
                if (BenchmarkTestResult.TryInterpolate(_curveScratch, test.Reference, out var ms))
                {
                    header = CreateGradedHeader(test, primary, ms, $"({FormatMs(ms)} for {reference})");
                }
                else if (result.StopReason.StartsWith("Above"))
                {
                    header = CreateGradedHeader(test, primary, double.MaxValue, $"(> {BenchmarkRunner.StopAboveMs:0}ms for {reference})");
                }
            }

            _headers[index] = header;
        }
    }

    private static TestHeader CreateGradedHeader(BenchmarkTest test, BenchmarkRun run, double referenceMs, string detail)
    {
        var grade = test.GetGrade(referenceMs, run.System.IsDebugBuild);
        return new TestHeader(_gradeLabels[(int)grade], ColorForGrade(grade), detail);
    }

    private static Color ColorForGrade(BenchmarkGrade grade)
    {
        return grade switch
                   {
                       <= BenchmarkGrade.AMinus => UiColors.StatusOkay,
                       <= BenchmarkGrade.CMinus => UiColors.StatusActivated,
                       _                        => UiColors.StatusWarning,
                   };
    }

    private readonly record struct TestHeader(string Grade, Color GradeColor, string Detail);

    private static void DrawCurveLine(ImDrawListPtr drawList, ImRect plot, BenchmarkTest test, List<CurvePoint> curve, Color color, float thickness)
    {
        var minLoadLog = Math.Log10(test.MinLoad);
        var maxLoadLog = Math.Log10(test.MaxLoad);
        for (var index = 1; index < curve.Count; index++)
        {
            var a = new Vector2(XForLoad(plot, curve[index - 1].Load, minLoadLog, maxLoadLog), YForMs(plot, curve[index - 1].MeanMs));
            var b = new Vector2(XForLoad(plot, curve[index].Load, minLoadLog, maxLoadLog), YForMs(plot, curve[index].MeanMs));
            drawList.AddLine(a, b, color, thickness);
        }
    }

    private static void DrawBand(ImDrawListPtr drawList, ImRect plot, BenchmarkTest test, List<CurvePoint> curve)
    {
        var minLoadLog = Math.Log10(test.MinLoad);
        var maxLoadLog = Math.Log10(test.MaxLoad);
        var color = UiColors.StatusActivated.Fade(0.3f);
        for (var index = 1; index < curve.Count; index++)
        {
            var previous = curve[index - 1];
            var current = curve[index];
            var x0 = XForLoad(plot, previous.Load, minLoadLog, maxLoadLog);
            var x1 = XForLoad(plot, current.Load, minLoadLog, maxLoadLog);
            drawList.AddQuadFilled(new Vector2(x0, YForMs(plot, previous.MaxMs)),
                                   new Vector2(x1, YForMs(plot, current.MaxMs)),
                                   new Vector2(x1, YForMs(plot, current.MinMs)),
                                   new Vector2(x0, YForMs(plot, previous.MinMs)),
                                   color);
        }
    }

    private static float XForLoad(ImRect plot, double load, double minLoadLog, double maxLoadLog)
    {
        var t = (Math.Log10(load) - minLoadLog) / (maxLoadLog - minLoadLog);
        return (float)(plot.Min.X + t * (plot.Max.X - plot.Min.X));
    }

    /** Log scale from <see cref="MinPlottedMs"/> at the bottom to <see cref="BenchmarkRunner.StopAboveMs"/> at the top. */
    private static float YForMs(ImRect plot, double ms)
    {
        var minLog = Math.Log10(MinPlottedMs);
        var maxLog = Math.Log10(BenchmarkRunner.StopAboveMs);
        var t = (Math.Log10(Math.Max(ms, MinPlottedMs)) - minLog) / (maxLog - minLog);
        t = Math.Clamp(t, 0, 1);
        return (float)(plot.Max.Y - t * (plot.Max.Y - plot.Min.Y));
    }

    /** ImGui's Text, TextColored and TextWrapped read their argument as a printf format, where % starts a code. */
    private static string EscapeFormat(string text) => text.Replace("%", "%%");

    /** Wraps at the window edge without treating the text as a format string; notes are free text. */
    private static void TextWrappedUnformatted(string text)
    {
        ImGui.PushTextWrapPos(0);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
    }

    private static Color GetCompareColor(int index) => _compareColors[index % _compareColors.Length];

    private static string FormatMs(double ms) => ms < 1 ? $"{ms:0.00}ms" : ms < 10 ? $"{ms:0.0}ms" : $"{ms:0}ms";

    private static string BuildSummary(BenchmarkRun primary)
    {
        var runs = new List<BenchmarkRun> { primary };
        foreach (var run in _savedRuns)
        {
            if (run.FilePath != primary.FilePath && _comparedPaths.Contains(run.FilePath))
                runs.Add(run);
        }

        var curve = new List<CurvePoint>();
        var summary = new StringBuilder();
        summary.Append("| Test | Reference |");
        foreach (var run in runs)
        {
            summary.Append(' ').Append(run.Label).Append(" (").Append(run.System.Adapter).Append(") |");
        }

        summary.AppendLine();
        summary.Append("|---|---|");
        foreach (var _ in runs)
        {
            summary.Append("---|");
        }

        summary.AppendLine();
        foreach (var test in BenchmarkSuite.Tests)
        {
            summary.Append("| ").Append(test.Category).Append(" | ")
                   .Append(BenchmarkRunner.FormatLoad(test.Reference)).Append(' ').Append(test.UnitLabel).Append(" |");
            foreach (var run in runs)
            {
                var text = "–";
                if (run.TryGetTest(test.Key, out var result))
                {
                    result.GetCurve(test.PlotsCpuTime, curve);
                    if (BenchmarkTestResult.TryInterpolate(curve, test.Reference, out var ms))
                    {
                        text = $"{FormatMs(ms)} ({_gradeLabels[(int)test.GetGrade(ms, run.System.IsDebugBuild)]})";
                    }
                    else if (result.StopReason.StartsWith("Above"))
                    {
                        text = $"> {BenchmarkRunner.StopAboveMs:0}ms";
                    }
                }

                summary.Append(' ').Append(text).Append(" |");
            }

            summary.AppendLine();
        }

        summary.AppendLine();
        foreach (var run in runs)
        {
            summary.Append("- ").Append(run.DisplayName).Append(": ").Append(run.System.Backend).Append(", ")
                   .Append(run.System.Driver).Append(", ").Append(run.System.Os).Append(", TiXL ")
                   .Append(run.System.TixlVersion).Append(' ').Append(run.System.BuildConfiguration)
                   .Append(", power: ").AppendLine(run.System.PowerSource);
        }

        return summary.ToString();
    }

    /// <summary>Reloads after a run was saved, and whenever the window reappears, to pick up files copied into the folder.</summary>
    private static void ReloadSavedRunsIfNeeded()
    {
        var frame = ImGui.GetFrameCount();
        var wasHidden = frame - _lastDrawnFrame > 1;
        _lastDrawnFrame = frame;
        if (_loadedRunsVersion == BenchmarkRunner.SavedRunsVersion && _hasLoaded && !wasHidden)
            return;

        _savedRuns = BenchmarkRun.LoadAll();
        _historyRowsAreFresh = false;
        _loadedRunsVersion = BenchmarkRunner.SavedRunsVersion;
        _hasLoaded = true;
    }

    private const float MinGraphWidth = 320;
    private const float GraphHeight = 250;
    private const float GraphSpacing = 12;
    private const float HeaderGap = 8;
    private const float GradeGap = 6;
    private const float PanelPadding = 14;
    private const float TooltipWrapWidth = 320;
    private const double MinPlottedMs = 0.1;

    private static readonly string[] _gradeLabels = ["A+", "A", "A-", "B+", "B", "B-", "C+", "C", "C-", "D+", "D", "D-", "F"];

    private static readonly (double Ms, string Label)[] _durationLines =
        [(0.1, "0.1ms"), (1, "1ms"), (8, "8ms"), (16, "16ms"), (50, "50ms")];

    private static readonly Color[] _compareColors =
        [UiColors.ColorForValues, UiColors.ColorForTextures, UiColors.ColorForCommands, UiColors.ColorForGpuData];

    private static List<BenchmarkRun> _savedRuns = [];
    private static int _loadedRunsVersion;
    private static bool _hasLoaded;
    private static int _lastDrawnFrame;
    private static readonly List<HistoryRow> _historyRows = [];
    private static BenchmarkRun? _historyPrimary;
    private static int _historyRunsVersion;
    private static bool _historyRowsAreFresh;
    private static readonly HashSet<string> _comparedPaths = [];
    private static readonly List<BenchmarkRun> _comparedRuns = [];
    private static readonly List<CurvePoint> _curve = [];
    private static readonly List<CurvePoint> _curveScratch = [];
    private static readonly TestHeader[] _headers = new TestHeader[BenchmarkSuite.Tests.Length];
    private static BenchmarkRun? _headersRun;
    private static int _headersStepCount;
    private static int _tooltipTestIndex;
    private static int _tooltipLoad;
    private static BenchmarkTestResult? _tooltipResult;
}
