#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.IO;
using T3.Core.UserData;
using T3.Serialization;

// ReSharper disable MemberCanBeInternal

namespace T3.Editor.Skills.Data;

/// <summary>
/// Defines how Zones, Topics and Levels are connected and layed out on a map
/// </summary>
internal sealed class SkillMapData
{
    public readonly List<QuestZone> Zones = [];

    [Newtonsoft.Json.JsonIgnore]
    public static QuestZone FallbackZone = new() { Title = "_undefined", Id = QuestZone.FallBackZoneId };

    public readonly List<QuestTopic> Topics = [];
    
    public static bool TryGetTopicWithNamespace(string symbolNamespace, [NotNullWhen(true)] out QuestTopic? questTopic)
    {
        foreach (var t in Data.Topics)
        {
            if (t.Namespace != symbolNamespace)
                continue;

            questTopic = t;
            return true;
        }

        questTopic = null;
        return false;
    }

    public static bool TryGetTopic(Guid id, [NotNullWhen(true)] out QuestTopic? questTopic)
    {
        foreach (var t in Data.Topics)
        {
            if (t.Id != id)
                continue;

            questTopic = t;
            return true;
        }

        questTopic = null;
        return false;
    }

    public static bool TryGetZone(Guid id, [NotNullWhen(true)] out QuestZone? zone)
    {
        foreach (var z in Data.Zones)
        {
            if (z.Id != id)
                continue;

            zone = z;
            return true;
        }

        zone = null;
        return false;
    }

    #region serialization
    internal static void Load()
    {
        if (!File.Exists(SkillMapPath))
        {
            Data = new SkillMapData(); // Fallback
        }

        try
        {
            Data = JsonUtils.TryLoadingJson<SkillMapData>(SkillMapPath)!;
            if (Data == null)
                throw new Exception("Failed to load SkillProgress");
        }
        catch (Exception e)
        {
            Log.Error($"Failed to load {SkillMapPath} : {e.Message}");
            Data = new SkillMapData();
        }

        foreach (var z in Data.Zones)
        {
            if (z.Id == QuestZone.FallBackZoneId)
            {
                FallbackZone = z;
                return;
            }
        }

        Data.Zones.Add(FallbackZone);
    }

    internal static void Save()
    {
        Directory.CreateDirectory(FileLocations.ReadOnlySettingsPath);
        JsonUtils.TrySaveJson(Data, SkillMapPath);
    }

    private static string SkillMapPath => Path.Combine(FileLocations.ReadOnlySettingsPath, "SkillMap.json");

    [Newtonsoft.Json.JsonIgnore]
    internal static SkillMapData Data = new();
    #endregion
}

internal sealed class QuestZone
{
    public Guid Id = Guid.NewGuid();
    public string Title = string.Empty;


    [Newtonsoft.Json.JsonIgnore]
    public static readonly Guid FallBackZoneId = new("717505B4-0C6E-4708-85D8-54E202F9BBDF");
}