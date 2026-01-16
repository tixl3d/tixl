using System.Diagnostics.CodeAnalysis;
using System.IO;
using Newtonsoft.Json;
using T3.Core.UserData;
using T3.Serialization;
// ReSharper disable MemberCanBeInternal

namespace T3.Editor.Skills.Data;

/// <summary>
/// The active user's progress for serialization to settings.
/// </summary>
public sealed class SkillProgress
{
    public Guid ActiveTopicId;

    public List<LevelResult> Results = [];

    public sealed class LevelResult
    {
        public Guid TopicId;
        public Guid LevelSymbolId;

        public DateTime StartTime;
        public DateTime EndTime;

        [JsonConverter(typeof(SafeEnumConverter<States>))]
        public States State;

        public int Rating;

        public enum States
        {
            Undefined,
            Started,
            Skipped,
            Completed,
        }
    }

    #region serialization
    internal static void LoadUserData()
    {
        if (!File.Exists(SkillProgressPath))
        {
            Data = new SkillProgress(); // Fallback
        }

        try
        {
            Data = JsonUtils.TryLoadingJson<SkillProgress>(SkillProgressPath)!;
            if (Data == null)
                throw new Exception("Failed to load SkillProgress");
        }
        catch (Exception e)
        {
            Log.Error($"Failed to load {SkillProgressPath} : {e.Message}");
            Data = new SkillProgress();
        }
    }

    internal static void SaveUserData()
    {
        Directory.CreateDirectory(FileLocations.SettingsDirectory);
        JsonUtils.TrySaveJson(Data, SkillProgressPath);
    }

    internal static void SaveLevelResult(QuestLevel level, SkillProgress.LevelResult result)
    {
        Data.Results.Add(result);
        SaveUserData();
    }

    private static string SkillProgressPath => Path.Combine(FileLocations.SettingsDirectory, "SkillProgress.json");

    [Newtonsoft.Json.JsonIgnore]
    internal static SkillProgress Data = new ();

    internal static bool TryGetLastResult([NotNullWhen(true)] out SkillProgress.LevelResult result)
    {
        result = null;
        if (Data.Results.Count == 0)
            return false;

        result= Data.Results[^1];
        return true;
    }
    
    #endregion
}