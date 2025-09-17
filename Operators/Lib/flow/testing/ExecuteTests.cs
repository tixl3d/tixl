using System.Diagnostics;
using T3.Core.Animation;
using T3.Core.Utils;

// ReSharper disable ForCanBeConvertedToForeach

namespace Lib.flow.testing;

[Guid("83cb923e-a387-4be2-b391-4111c7bd90fe")]
internal sealed class ExecuteTests : Instance<ExecuteTests>
{
    [Output(Guid = "229A2DD4-419F-43B9-AECD-12EAB9B25DEF")]
    public readonly Slot<string> Result = new();

    public ExecuteTests()
    {
        Result.UpdateAction = Update;
    }

    private bool _isRunning;
    private int _testInputIndex;
    private int _testframeId;

    private void Update(EvaluationContext context)
    {
        var testframeId = "_TestFrame";
        var testResultId = "_TestResult";
        var testActionId = "_TestAction";

        var onlyShowFails = OnlyShowFails.GetValue(context);

        var isTestRoot = !context.IntVariables.TryGetValue(testframeId, out var frame);

        if (isTestRoot)
        {
            context.IntVariables[testframeId] = Playback.FrameCount;
        }
        else
        {
            //Log.Debug("Forwarding test...", this);
        }

        var needsUpdate = MathUtils.WasChanged(TriggerTest.GetValue(context), ref _testTriggered);
        needsUpdate |= MathUtils.WasChanged(UpdateReferences.GetValue(context), ref _updateReferences);

        var subTextRestarted = false;
        if (!isTestRoot)
        {
            if (context.ObjectVariables[testActionId] != null
                && context.IntVariables[testframeId] != _testframeId)
            {
                subTextRestarted = true;
                _testframeId = context.IntVariables[testframeId];
            }
        }
        
        if (isTestRoot && !needsUpdate && !_isRunning)
        {
            return;
        }

        if (subTextRestarted || needsUpdate && (_testTriggered || _updateReferences))
        {
            _stopwatch.Restart();
            Playback.OpNotReady = false;
            _isRunning = true;
            Result.DirtyFlag.Trigger = DirtyFlagTrigger.Animated;
            _testInputIndex = 0;
            
            
            _rootTestResults.Clear();
        }
        
        if (isTestRoot)
        {
            if (_updateReferences)
            {
                context.ObjectVariables[testActionId] = "UpdateReferences";
                //Log.Debug("Updating references ..." + testIndex, this);
            }
            else if (_testTriggered)
            {
                context.ObjectVariables[testActionId] = "Test";
                //Log.Debug("Testing ..." + testIndex, this);
            }
            else
            {
                return;
            }

            context.ObjectVariables[testResultId] = _rootTestResults;
        }


        var testSlots = Tests.CollectedInputs;

        // Execute test commands
        while (_testInputIndex < testSlots.Count)
        {
            //Log.Debug($" {_testInputIndex} / {testSlots.Count}",this);
            testSlots[_testInputIndex].DirtyFlag.ForceInvalidate();
            testSlots[_testInputIndex].GetValue(context);
            
            if (Playback.OpNotReady)
                break;

            _testInputIndex++;
        }

        if (!isTestRoot)
        {
            Result.Value = "intermediate test";
            return;
        }
        
        if (!context.IntVariables.Remove(testframeId))
            Log.Warning($"Expected {testframeId} variable for root test");

        if (Playback.OpNotReady)
            return;

        _stopwatch.Stop();

        _stringBuilder.Clear();
        var countFails = 0;
        var countSuccess = 0;
        foreach (var line in _rootTestResults)
        {
            if (line.Contains("FAILED"))
            {
                _stringBuilder.AppendLine(line);
                countFails++;
            }
            else if (line.Contains("PASSED"))
            {
                countSuccess++;
                if (!onlyShowFails)
                    _stringBuilder.AppendLine(line);
            }
        }

        var countTotal = countFails + countSuccess;
        var passedLabel = countFails == 0 ? "SUCCESS" : "FAILED";

        _stringBuilder.Insert(0, $"{passedLabel}:   {countSuccess} / {countTotal}  {_stopwatch.ElapsedMilliseconds * 0.001:0.0s}\n\n");

        Result.Value = _stringBuilder.ToString();

        if (!Playback.OpNotReady)
        {
            Log.Debug("Completed", this);
            TriggerTest.SetTypedInputValue(false);
            UpdateReferences.SetTypedInputValue(false);
            Result.DirtyFlag.Trigger = DirtyFlagTrigger.None;
            _isRunning = false;
        }
    }

    private readonly StringBuilder _stringBuilder = new();
    private readonly List<string> _rootTestResults = [];
    private bool _testTriggered;
    private bool _updateReferences;

    private readonly Stopwatch _stopwatch = new();

    [Input(Guid = "18023689-423A-4FB8-BC3C-9E74D0148C78")]
    public readonly MultiInputSlot<string> Tests = new();

    [Input(Guid = "31937a13-1e53-45dd-8e6f-91ca5f3aaa19")]
    public readonly InputSlot<bool> TriggerTest = new();

    [Input(Guid = "E1989C94-8F51-414A-9CA0-33631875A9DF")]
    public readonly InputSlot<bool> UpdateReferences = new();

    [Input(Guid = "13D66A6D-75E1-4AB0-805D-A2234B3334A4")]
    public readonly InputSlot<bool> OnlyShowFails = new();
}