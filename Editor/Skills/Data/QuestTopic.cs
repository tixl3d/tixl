using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;
using T3.Editor.Skills.Ui;
using T3.Serialization;

namespace T3.Editor.Skills.Data;

[SuppressMessage("ReSharper", "MemberCanBeInternal")]
public sealed class QuestTopic
{
    public Guid Id = Guid.Empty;
    public string Title = string.Empty;
    public string Description = string.Empty;

    public Vector2 MapCoordinate;
    public Guid ZoneId;

    public List<Guid> UnlocksTopics = [];

    /** For linking to package levels */
    public string Namespace;

    [JsonConverter(typeof(SafeEnumConverter<TopicTypes>))]
    public TopicTypes TopicType;

    [JsonIgnore]
    public ProgressStates ProgressionState;

    [JsonConverter(typeof(SafeEnumConverter<Requirements>))]
    public Requirements Requirement = Requirements.None;

    public override string ToString()
    {
        return Title;
    }

    #region runtime data 
    
    /** Levels will be initialized from symbols in a Skills package */
    [JsonIgnore]
    public List<QuestLevel> Levels = [];

    [JsonIgnore]
    public int CompletedLevelCount;
    
    [JsonIgnore]
    public int SkippedLevelCount;

    [JsonIgnore]
    public HashSet<Guid> RequiredTopicIds = [];

    #endregion
    
    
    public enum Requirements
    {
        None,
        IsValidStartPoint,
        AnyInputPath,
        AllInputPaths,
    }

    public enum ProgressStates
    {
        None,
        
        /** Planned topic (created but no levels added. */
        Upcoming,
        
        /** temp state during progress calculation */
        NoResultsYet,  
        
        /** Not all required topics completed */
        Locked,
        
        /** All requirements are met */
        Unlocked,
        
        /** The currently played level */
        Active,
        
        /** Topic started but not all completed */
        Started,
        
        /** Completed by skipped some levels */
        Passed,
        
        /** Completed all levels */
        Completed,
    }

    /** We use an enum to avoid types in serialization. */
    public enum TopicTypes
    {
        Image,
        Numbers,
        Command,
        String,
        Gpu,
        ShaderGraph,
    }

    [JsonIgnore]
    internal HexCanvas.Cell Cell { get => new((int)MapCoordinate.X, (int)MapCoordinate.Y); set => MapCoordinate = new Vector2(value.X, value.Y); }
}