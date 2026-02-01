#nullable enable
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using ImGuiNET;
using T3.Core.Animation;
using T3.Core.Model;
using T3.Editor.Gui;
using T3.Editor.Gui.Hub;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.MagGraph.Interaction;
using T3.Editor.Gui.MagGraph.Ui;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Window;
using T3.Editor.Gui.Windows.Layouts;
using T3.Editor.Gui.Windows.Output;
using T3.Editor.Skills.Data;
using T3.Editor.Skills.Ui;
using T3.Editor.UiModel;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Skills.Training;

/// <summary>
/// Handles playing skill map topics and levels
/// </summary>
internal static partial class SkillTraining
{
    internal static void Initialize()
    {
        SkillProgress.LoadUserData();
        SkillMapData.Load();
        InitializeSkillMapFromLevelSymbols();
        UpdateTopicStatesAndProgression();
    }

    public static void SetGraphWindow(GraphWindow graphWindow)
    {
        _context.GraphWindow = graphWindow;
    }
    
    public static void StartTopic(QuestTopic topic)
    {
        SkillProgress.Data.ActiveTopicId = topic.Id;
        SkillProgress.SaveUserData();
        UpdateTopicStatesAndProgression();
        StartActiveLevel();
    }
    
    public static void StartTopicLevel(QuestTopic topic, QuestLevel level)
    {
        if (!topic.Levels.Contains(level))
            return;
        
        SkillProgress.Data.ActiveTopicId = topic.Id;
        _context.ActiveLevel = level;
        StartActiveLevel();
    }

    public static void ReplayLevel(QuestLevel level)
    {
        if (_context.ActiveTopic == null || !_context.ActiveTopic.Levels.Contains(level))
        {
            Log.Warning($"Active topic doesn't contain {level}");
            return;
        }

        
        ExitPlayMode();
        _context.ActiveLevel = level;
        StartActiveLevel();
    }

    private static bool _savedOriginalLayout;

    internal record UiState
    {
        
    }
    
    internal static void StartActiveLevel(bool forceSaveUiState= false)
    {
        if (_context.GraphWindow == null)
        {
            Debug.Assert(GraphWindow.GraphWindowInstances.Count > 0);
            _context.GraphWindow = GraphWindow.GraphWindowInstances[0];
        }
        
        Debug.Assert(_context.GraphWindow != null);
        Debug.Assert(_context.ActiveTopic != null);
        Debug.Assert(_context.ActiveLevel != null);

        if (!TryGetSkillsProject(out var skillProject) || _context.ActiveLevel == null)
        {
            Log.Error("Can't find skills project");
            return;
        }

        if (!OpenedProject.TryCreateWithExplicitHome(skillProject,
                                                     _context.ActiveLevel.SymbolId,
                                                     out var openedProject,
                                                     out var failureLog))
        {
            Log.Warning(failureLog);
            return;
        }

        SwitchToMinimalLayout(forceSaveUiState);

        // Check if for some reason Output window is not accessible after loading layout
        if (!OutputWindow.TryGetPrimaryOutputWindow(out var outputWindow))
        {
            Log.Debug("Can't access primary output window");
            RestoreUi();
            _context.StateMachine.SetState(SkillTrainingStates.Inactive, _context);
            return;
        }
        
        UiConfig.HideAllUiElements();
        UserSettings.Config.GraphStyle = UserSettings.GraphStyles.Magnetic;
        
        UserSettings.Config.EnableIdleMotion = true;
        Playback.Current.TimeInBars = 0;
        
        SkillProgress.Data.ActiveTopicId = _context.ActiveTopic.Id;
        
        _context.GraphWindow.TrySetToProject(openedProject, tryRestoreViewArea: false);
        _context.ProjectView = _context.GraphWindow.ProjectView;
        _context.ProjectView?.FocusViewToSelection();
        _context.OpenedProject = openedProject;

        Debug.Assert(_context.OpenedProject != null);
        
        // Pin output
        var rootInstance = _context.OpenedProject.Structure.GetRootInstance();
        if (rootInstance == null)
        {
            Log.Debug("Failed to load root");
            RestoreUi();
            _context.StateMachine.SetState(SkillTrainingStates.Inactive, _context);
            return;
        }

        outputWindow.Pinning.PinInstance(rootInstance);
        TourInteraction.SetProgressIndex(rootInstance.Symbol.Id, 0);

        FitViewToSelectionHandling.FitViewToSelection();

        _context.StateMachine.SetState(SkillTrainingStates.Playing, _context);
    }



    internal static void Update()
    {
        var playmodeEnded = _context.ProjectView?.GraphView is { Destroyed: true };
        if (_context.StateMachine.CurrentState != SkillTrainingStates.Inactive && playmodeEnded)
        {
            _context.StateMachine.SetState(SkillTrainingStates.Inactive, _context);
        }

        if (_context.StateMachine.CurrentState == SkillTrainingStates.Completed)
        {
            SkillProgressionPopup.Draw();
        }

        _context.StateMachine.UpdateAfterDraw(_context);
    }

    /// <summary>
    /// This is called after processing of a frame and can be used to access the output evaluation context
    /// </summary>
    public static void PostUpdate()
    {
        if (_context.StateMachine.CurrentState != SkillTrainingStates.Playing)
            return;

        if (!OutputWindow.TryGetPrimaryOutputWindow(out var outputWindow))
        {
            Log.Warning("Can't find output window for playmode?!");
            return;
        }

        // Try to prevent saving accidental changes...
        _context.ProjectView?.CompositionInstance?.GetSymbolUi().ClearModifiedFlag();

        if (!outputWindow.EvaluationContext.FloatVariables.TryGetValue(PlayModeProgressVariableId, out var progress))
        {
            //Log.Warning($"Can't find progress variable '{PlayModeProgressVariableId}' after evaluation?");
            progress = 1;
            //return;
        }

        if (_context.StateMachine.StateTime > 0.2f && progress >= 1.0f)
        {
            _context.StateMachine.SetState(SkillTrainingStates.Completed, _context);
            SaveNewResult(SkillProgress.LevelResult.States.Completed);
            UpdateTopicStatesAndProgression(); 
            SkillProgressionPopup.Show();
        }
    }

    internal static void CompleteAndProgressToNextLevel()
    {
        ExitPlayMode();
        StartActiveLevel();
    }

    internal static void SaveNewResult(SkillProgress.LevelResult.States resultState)
    {
        if (_context.ActiveTopic == null || _context.ActiveLevel == null)
            return;

        SkillProgress.SaveLevelResult(_context.ActiveLevel, new SkillProgress.LevelResult
                                                                {
                                                                    TopicId = _context.ActiveTopic.Id,
                                                                    LevelSymbolId = _context.ActiveLevel.SymbolId,
                                                                    StartTime = DateTime.Now,
                                                                    EndTime = DateTime.Now,
                                                                    State = resultState,
                                                                    Rating = -1,
                                                                });
    }

    /// <summary>
    /// Update active topic and level from the last completed or skipped level in skill progression 
    /// </summary>
    internal static bool UpdateTopicStatesAndProgression()
    {
        _cache.UpdateCache();

        // Will be updated in the next pass
        foreach (var topic in SkillMapData.Data.Topics)
        {
            topic.RequiredTopicIds.Clear();
        }

        // Update topic states
        foreach (var topic in SkillMapData.Data.Topics)
        {
            if (topic.Levels.Count == 0)
            {
                topic.ProgressionState = QuestTopic.ProgressStates.Upcoming;
                continue;
            }

            foreach (var unlockedTopicId in topic.UnlocksTopics)
            {
                if (!_cache.TopicsById.TryGetValue(unlockedTopicId, out var unlockedTopic))
                {
                    Log.Warning($"Can't find topic id {unlockedTopicId} to unlock ?");
                    continue;
                }

                unlockedTopic.RequiredTopicIds.Add(topic.Id);
            }

            var someLevelsNotCompleted = false;
            var someLevelsSkipped = false;
            topic.CompletedLevelCount = 0;

            foreach (var level in topic.Levels)
            {
                level.LevelState = SkillProgress.LevelResult.States.Undefined;
                if (!_cache.ResultsForLevelId.TryGetValue(level.SymbolId, out var results))
                {
                    topic.ProgressionState = QuestTopic.ProgressStates.NoResultsYet;
                    someLevelsNotCompleted = true;
                    continue;
                }

                var resultsSkipped = 0;
                var resultsCompleted = 0;

                foreach (var r in results)
                {
                    switch (r.State)
                    {
                        case SkillProgress.LevelResult.States.Skipped:
                            resultsSkipped++;
                            break;
                        case SkillProgress.LevelResult.States.Completed:
                            resultsCompleted++;
                            break;
                    }
                }

                if (resultsCompleted > 0)
                {
                    topic.CompletedLevelCount++;
                    level.LevelState = SkillProgress.LevelResult.States.Completed;
                    continue;
                }

                if (resultsSkipped > 0)
                {
                    level.LevelState = SkillProgress.LevelResult.States.Skipped;
                    topic.SkippedLevelCount++;
                    someLevelsSkipped = true;
                    continue;
                }

                someLevelsNotCompleted = true;
            }

            if (topic.CompletedLevelCount == 0 && topic.SkippedLevelCount == 0)
            {
                topic.ProgressionState = QuestTopic.ProgressStates.NoResultsYet;
                continue;
            }

            if (someLevelsNotCompleted)
            {
                topic.ProgressionState = QuestTopic.ProgressStates.Started;
                continue;
            }

            if (someLevelsSkipped)
            {
                topic.ProgressionState = QuestTopic.ProgressStates.Passed;
                continue;
            }

            topic.ProgressionState = QuestTopic.ProgressStates.Completed;
        }

        // Flood fill unlocking...
        while (TryUnlockMoreTopics()) ;
        
        
        if(!_cache.TopicsById.TryGetValue(SkillProgress.Data.ActiveTopicId, out var activeTopic ) 
           || activeTopic.Levels.Count ==0)
        {
            if (SkillMapData.Data.Topics.Count == 0 || SkillMapData.Data.Topics[0].Levels.Count == 0)
            {
                Log.Warning("No skill quest levels found?");
                _context.ActiveTopic = null;
                _context.ActiveLevel = null;
                return false;
            }
            
            _context.ActiveTopic = SkillMapData.Data.Topics[0];
            _context.ActiveLevel = _context.ActiveTopic.Levels[0];
            
            Log.Debug($"Reset active skill topic to '{_context.ActiveTopic}'");
            return true;
        }

        QuestLevel activeLevel=null!;
        for (var index = 0; index < activeTopic.Levels.Count; index++)
        {
            activeLevel = activeTopic.Levels[index];
            if (activeLevel.LevelState == SkillProgress.LevelResult.States.Undefined)
                break;
        }

        _context.ActiveTopic = activeTopic;
        _context.ActiveLevel = activeLevel;
        return true;
    }

    /** Returns true if at least one topic got unlocked */
    private static bool TryUnlockMoreTopics()
    {
        var anyUnlocked = false;

        foreach (var topic in SkillMapData.Data.Topics)
        {
            if (topic.ProgressionState != QuestTopic.ProgressStates.NoResultsYet)
                continue;

            if (topic.RequiredTopicIds.Count == 0)
            {
                topic.ProgressionState = QuestTopic.ProgressStates.Unlocked;
                anyUnlocked = true;
            }

            var allUnlocked = true;
            foreach (var requiredId in topic.RequiredTopicIds)
            {
                if (!_cache.TopicsById.TryGetValue(requiredId, out var requiredTopic))
                {
                    // Would have warned earlier
                    continue;
                }

                var unlocked = requiredTopic.ProgressionState == QuestTopic.ProgressStates.Completed
                               || requiredTopic.ProgressionState == QuestTopic.ProgressStates.Passed;

                if (!unlocked)
                    allUnlocked = false;
            }

            if (!allUnlocked)
            {
                topic.ProgressionState = QuestTopic.ProgressStates.Locked;    
                continue;
            }

            topic.ProgressionState = QuestTopic.ProgressStates.Unlocked;
            anyUnlocked = true;
        }

        return anyUnlocked;
    }


    private static bool TryGetSkillsProject([NotNullWhen(true)] out EditorSymbolPackage? skillProject)
    {
        skillProject = null;
        var allProjects = SymbolPackage.AllPackages;
        
        //var allProjects = SymbolPackage.AllPackages;
        foreach (var p in allProjects)
        {
            if (p is not EditorSymbolPackage editorProject)
                continue;
            
            if (editorProject.Name == "Skills")
            {
                skillProject = editorProject;
                return true;
            }
        }

        return false;
    }

    internal static void ExitPlayMode()
    {
        Debug.Assert(_context.OpenedProject != null);
        if (_context.OpenedProject == null)
            return;

        if (!_context.OpenedProject.Package.SymbolUis.TryGetValue(_context.OpenedProject.Package.HomeSymbolId, out var homeSymbolId))
        {
            Log.Warning($"Can't find symbol to revert changes?");
            return;
        }

        _context.ProjectView?.Close();
        _context.OpenedProject.Package.Reload(homeSymbolId);
        _context.StateMachine.SetState(SkillTrainingStates.Inactive, _context);
    }

    private static void SwitchToMinimalLayout(bool forceSaveUiState)
    {
        // Keep the original UI only once because it might not be
        // fully restored between level jumps.
        if (!_savedOriginalLayout || forceSaveUiState)
        {
            _context.PreviousUiState = UiConfig.KeepUiState();
            _savedOriginalLayout = true;
        }

        // Switch layout
        LayoutHandling.LoadAndApplyLayoutOrFocusMode(LayoutHandling.Layouts.SkillQuest);
        UserSettings.SaveDisabled = true;
    }

    private static void RestoreUi()
    {
        if(_context.PreviousUiState != null)
            UiConfig.ApplyUiState(_context.PreviousUiState);

        UserSettings.SaveDisabled = false;
    }    
    
    public static bool IsInPlayMode => (_context.StateMachine.CurrentState == SkillTrainingStates.Playing ||
                                        _context.StateMachine.CurrentState == SkillTrainingStates.Completed);

    public static void DrawLevelHeader()
    {
        var test1 = _context.StateMachine.CurrentState == SkillTrainingStates.Playing;
        var test2 = _context.StateMachine.CurrentState == SkillTrainingStates.Completed;

        if (!test1 && !test2)
            return;

        var topic = _context.ActiveTopic;
        var level = _context.ActiveLevel;
        if (topic == null || level == null)
            return;

        var levelIndex = topic.Levels.IndexOf(level);

        var indentation = 40 * T3Ui.UiScaleFactor;

        FormInputs.AddVerticalSpace();
        ImGui.Indent(indentation);

        ImGui.PushFont(Fonts.FontSmall);
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        ImGui.TextUnformatted($"{topic.Title}  {levelIndex + 1}/{topic.Levels.Count} ");
        ImGui.PopStyleColor();
        ImGui.PopFont();

        var keepCursor = ImGui.GetCursorPos();
        ImGui.SetCursorPos(keepCursor - new Vector2(1f, -0.15f) * ImGui.GetFrameHeight());

        if (CustomComponents.TransparentIconButton(Icon.Exit, Vector2.Zero))
        {
            _context.ProjectView?.Close();
        }

        ImGui.SetCursorPos(keepCursor);

        //ImGui.SameLine(0,10);

        CustomComponents.StylizedText(level.Title, Fonts.FontLarge, UiColors.TextMuted);
        if (_context.StateMachine.CurrentState == SkillTrainingStates.Completed && !SkillProgressionPopup.IsOpen)
        {
            var cursorPos = ImGui.GetCursorScreenPos();
            var label = "Solved! Show Results...";
            ImGui.PushFont(Fonts.FontLarge);
            var labelSize = ImGui.CalcTextSize(label);
            ImGui.SetCursorScreenPos(ImGui.GetWindowPos() + new Vector2(ImGui.GetWindowSize().X - labelSize.X-40, 20));
            
            //ImGui.SameLine(0,10);
            ImGui.PushStyleColor(ImGuiCol.Button, UiColors.BackgroundActive.Rgba);
            if (ImGui.Button(label))
            {
                SkillProgressionPopup.IsOpen = true;
            }
            ImGui.PopStyleColor();
            ImGui.PopFont();
            ImGui.SetCursorScreenPos(cursorPos);
        }

        ImGui.Unindent(indentation);
    }

    public static bool TryGetActiveTopicAndLevel([NotNullWhen(true)] out QuestTopic? topic,
                                                 [NotNullWhen(true)] out QuestLevel? level)
    {
        level = null;
        topic = null;
        if (_context.ActiveTopic == null || _context.ActiveLevel == null)
            return false;

        topic = _context.ActiveTopic;
        level = _context.ActiveLevel;
        return true;
    }

    //private static bool IsInPlaymode => _context.StateMachine.CurrentState == SkillTrainingStates.Playing;
    private const string PlayModeProgressVariableId = "_PlayModeProgress";

    private static readonly SkillTrainingContext _context = new()
                                                                {
                                                                    StateMachine = new
                                                                        StateMachine<SkillTrainingContext>(typeof(SkillTrainingStates),
                                                                                 SkillTrainingStates.Inactive
                                                                            ),
                                                                };

    public static void ResetProgress()
    {
        SkillProgress.Data.Results.Clear();
        SkillProgress.Data.ActiveTopicId = SkillMapData.Data.Topics.Count >0 ? SkillMapData.Data.Topics[0].Id : Guid.Empty;
        SkillProgress.SaveUserData();
        UpdateTopicStatesAndProgression();
        SkillQuestPanel.NeedsUpdate = true;
    }

    private static readonly Cache _cache = new();

    /** Small helper class to speed up access. Updating this is slow and should only be done after data change. */
    private sealed class Cache
    {
        public void UpdateCache()
        {
            TopicsById.Clear();
            LevelsById.Clear();
            ResultsForLevelId.Clear();

            foreach (var result in SkillProgress.Data.Results)
            {
                if (!ResultsForLevelId.TryGetValue(result.LevelSymbolId, out var levelResults))
                {
                    levelResults = [];
                    ResultsForLevelId[result.LevelSymbolId] = levelResults;
                }

                levelResults.Add(result);
            }

            foreach (var topic in SkillMapData.Data.Topics)
            {
                TopicsById[topic.Id] = topic;
                foreach (var level in topic.Levels)
                {
                    LevelsById[level.SymbolId] = level;
                }
            }
        }

        public readonly Dictionary<Guid, QuestTopic> TopicsById = new();
        public readonly Dictionary<Guid, QuestLevel> LevelsById = new();
        public readonly Dictionary<Guid, List<SkillProgress.LevelResult>> ResultsForLevelId = new();
    }
}