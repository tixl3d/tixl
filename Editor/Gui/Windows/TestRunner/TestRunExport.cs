#nullable enable
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using T3.Core.Compilation;
using T3.Core.Settings;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.Windows.TestRunner;

/// <summary>
/// Serializes a finished <see cref="RunReport"/> to JSON: run metadata plus, per set, every step's
/// outcome and comment and a per-set tally. The tally is enough to draw progress bars later; the full
/// step list keeps the detail. Every finished run is auto-saved to <see cref="RunsDirectory"/>, and the
/// Summary screen can copy the same JSON to the clipboard.
/// </summary>
internal static class TestRunExport
{
    internal static string RunsDirectory => Path.Combine(FileLocations.SettingsDirectory, "TestRuns");

    internal static string BuildJson(RunReport run) => JsonConvert.SerializeObject(BuildDto(run), Formatting.Indented);

    /// <summary>Writes the run to <see cref="RunsDirectory"/> as <c>yyyyMMdd_HHmmss.json</c>. Best-effort.</summary>
    internal static void AutoSave(RunReport run)
    {
        try
        {
            Directory.CreateDirectory(RunsDirectory);
            var path = Path.Combine(RunsDirectory, $"{run.FinishedUtc:yyyyMMdd_HHmmss}.json");
            File.WriteAllText(path, BuildJson(run));
        }
        catch (Exception e)
        {
            Log.Warning($"Could not auto-save the test run: {e.Message}");
        }
    }

    private static RunDto BuildDto(RunReport run)
    {
        var dto = new RunDto
                      {
                          RunId = run.Id.ToString(),
                          AppVersion = RuntimeAssemblies.FormattedVersion,
                          CommitHash = _commitHash,
                          UserNick = UserSettings.Config.UserName,
                          StartedUtc = run.StartedUtc,
                          FinishedUtc = run.FinishedUtc,
                      };

        foreach (var set in run.Sets)
        {
            var setDto = new SetDto
                             {
                                 Id = set.Id,
                                 Title = set.Title,
                                 Scope = set.Scope,
                                 AddedInVersion = set.AddedInVersion,
                             };

            for (var stepIdx = 0; stepIdx < set.Steps.Count; stepIdx++)
            {
                var result = FindResult(run, set.Id, stepIdx);
                var outcome = result?.Outcome ?? Outcome.Pending;

                setDto.Steps.Add(new StepDto
                                     {
                                         Index = stepIdx,
                                         Title = set.Steps[stepIdx].Title,
                                         Outcome = outcome.ToString(),
                                         Comment = result?.Comment ?? string.Empty,
                                         TimestampUtc = result != null && result.TimestampUtc != default ? result.TimestampUtc : null,
                                     });

                setDto.Summary.Total++;
                switch (outcome)
                {
                    case Outcome.Pass: setDto.Summary.Pass++; break;
                    case Outcome.Fail: setDto.Summary.Fail++; break;
                    case Outcome.Other: setDto.Summary.Other++; break;
                    case Outcome.Skipped: setDto.Summary.Skipped++; break;
                    default: setDto.Summary.Pending++; break;
                }
            }

            dto.Sets.Add(setDto);
        }

        return dto;
    }

    private static StepResult? FindResult(RunReport run, string setId, int stepIdx)
    {
        foreach (var result in run.Results)
        {
            if (result.SetId == setId && result.StepIndex == stepIdx)
                return result;
        }

        return null;
    }

    private static readonly string _commitHash = ReadCommitHash();

    private static string ReadCommitHash()
    {
        // The build stamps the git SHA after a '+' in the informational version (see Editor.csproj).
        var info = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrEmpty(info))
            return string.Empty;

        var plus = info.IndexOf('+');
        if (plus < 0 || plus == info.Length - 1)
            return string.Empty;

        var hash = info[(plus + 1)..];
        return hash.Length > 12 ? hash[..12] : hash;
    }

    // Internal (not private) so TestRunResults can deserialize the same shape it reads back.
    internal sealed class RunDto
    {
        public string RunId = string.Empty;
        public string AppVersion = string.Empty;
        public string CommitHash = string.Empty;
        public string UserNick = string.Empty;
        public DateTime StartedUtc;
        public DateTime FinishedUtc;
        public List<SetDto> Sets = new();
    }

    internal sealed class SetDto
    {
        public string Id = string.Empty;
        public string Title = string.Empty;
        public string Scope = string.Empty;
        public string AddedInVersion = string.Empty;
        public SummaryDto Summary = new();
        public List<StepDto> Steps = new();
    }

    internal sealed class StepDto
    {
        public int Index;
        public string Title = string.Empty;
        public string Outcome = string.Empty;
        public string Comment = string.Empty;
        public DateTime? TimestampUtc;
    }

    internal sealed class SummaryDto
    {
        public int Total;
        public int Pass;
        public int Fail;
        public int Other;
        public int Skipped;
        public int Pending;
    }
}
