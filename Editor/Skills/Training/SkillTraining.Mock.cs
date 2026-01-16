#nullable enable
using T3.Editor.Skills.Data;
using T3.Editor.UiModel;

namespace T3.Editor.Skills.Training;

internal static partial class SkillTraining
{
    private static void InitializeSkillMapFromLevelSymbols()
    {
        if (!TryGetSkillsProject(out var skills))
            return;

        var lastNamespace = string.Empty;
        
        QuestTopic? topic = null;
        QuestTopic? lastTopic = null;
        
        foreach (var symbol in skills.Symbols.Values
                                     .OrderBy(c => c.Namespace)
                                     .ThenBy(c => c.Name))
        {
            var startingNewTopic = symbol.Namespace != lastNamespace;
            if (startingNewTopic)
            {
                if (topic != null)
                {
                    lastTopic = topic;
                }

                if (!SkillMapData.TryGetTopicWithNamespace(symbol.Namespace, out topic))
                {
                    Log.Warning($"Can't map skill topic '{symbol.Namespace}' to skill map.");
                    
                    topic = new QuestTopic
                                {
                                    Id = Guid.NewGuid(),
                                    Title = RemovePrefix(symbol.Namespace.Split(".").Last()),
                                    UnlocksTopics = lastTopic != null ? [lastTopic.Id] : [],
                                    Requirement = QuestTopic.Requirements.None,
                                    Levels = [],
                                    Namespace = symbol.Namespace,
                                    ZoneId = SkillMapData.FallbackZone.Id,
                                };
                    
                    //SkillMapData.FallbackZone.Topics.Add(topic);
                    SkillMapData.Data.Topics.Add(topic);
                }
                
                // If we found a predefined topic with the given names, we can assume that it
                // is already defined in the skill map.
                
                lastNamespace = symbol.Namespace;
            }

            if (topic == null)
                continue;

            var symbolUi = symbol.GetSymbolUi();
            var topicName = string.IsNullOrEmpty(symbolUi.Description)
                                ? RemovePrefix(symbol.Name)
                                : symbolUi.Description;
            
            topic.Levels.Add(new QuestLevel
                                 {
                                     Title = topicName,
                                     SymbolId = symbol.Id,
                                 });

        }
    }

    private static string RemovePrefix(string input)
    {
        var idx = input.IndexOf('_');
        return idx >= 0 && idx + 1 < input.Length
                   ? input[(idx + 1)..]
                   : input;
    }
}