using Newtonsoft.Json;

namespace T3.Editor.Skills.Data;

public sealed class QuestLevel
{
    public string Title = string.Empty;
    public Guid SymbolId = Guid.Empty;


    [JsonIgnore]
    public SkillProgress.LevelResult.States LevelState;
}