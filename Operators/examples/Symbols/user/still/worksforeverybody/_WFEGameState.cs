using T3.Core.Utils;

namespace Examples.user.still.worksforeverybody;

[Guid("1dfc9f6d-effa-407b-8f8d-5adf62504205")]
internal sealed class _WFEGameState : Instance<_WFEGameState>
{

    [Output(Guid = "BFEEC4E7-01AC-47A2-83F4-67EC8D1456EC")]
    public readonly Slot<int> LastScore = new();
        
    [Output(Guid = "7794EB92-94DD-46A3-9F73-691421B8DDEC")]
    public readonly Slot<int> TotalScore = new();

    [Output(Guid = "4CCCE7C0-B23F-44D2-BDAE-D88B6DF2F7B7")]
    public readonly Slot<bool> ScoreChanged = new();

    [Output(Guid = "BF20C1BB-5261-4AA2-AD80-EB69E2210B11")]
    public readonly Slot<string> LastResult = new();
        
    [Output(Guid = "301463A0-75C1-4B1A-8AF2-1048421A8BFE", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> P = new();
        
    public _WFEGameState()
    {
        TotalScore.UpdateAction += Update;
        ScoreChanged.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        if (TriggerReset.GetValue(context))
        {
            _score = 0;
            _scoredSceneIndices.Clear();
        }

        var timeDifferenceAmplify = TimeDifferenceAmplify.GetValue(context);
            
        var wasTriggered = MathUtils.WasTriggered(KeyPressed.GetValue(context), ref _wasPressed);
        var isActive = IsActive.GetValue(context);
        var invalidPressPenalty = InvalidPressPenalty.GetValue(context);
        var perfectTime = PerfectTime.GetValue(context);
        var scoreForScene = ScoreForScene.GetValue(context);
        var sceneIndex = SceneIndex.GetValue(context);

        var dt = (float)(context.Playback.TimeInBars - perfectTime);
        var rp = 1 / MathF.Max(timeDifferenceAmplify, 0.001f);
        var p = rp / (rp + MathF.Abs(dt));
        P.Value = p;
            
        var scoreDelta = 0;
            
        if (wasTriggered)
        {
            if (!isActive)
            {
                scoreDelta =(int)invalidPressPenalty;
                LastResult.Value = "Wasn't active";
            }
            else
            {
                if (_scoredSceneIndices.Contains(sceneIndex))
                {
                    //Log.Debug($"added score {scoreDelta}", this);
                    scoreDelta = 0;
                    LastResult.Value = "Too often!";
                    LastScore.Value = scoreDelta;
                        
                }
                else
                {
                    // var dt = (float)(context.Playback.TimeInBars - perfectTime);
                    // var rp = 1 / MathF.Max(timeDifferenceAmplify, 0.001f);
                    // var p = rp / (rp + MathF.Abs(dt));
                        
                    _scoredSceneIndices.Add(sceneIndex);

                    if (dt < 0)
                    {
                        LastResult.Value = "Too early!";
                        scoreDelta = -10;
                        LastScore.Value = scoreDelta;
                    }
                    else
                    {
                        scoreDelta = (int)(scoreForScene * p);
                        if (p > 0.5)
                        {
                            LastResult.Value = "Perfect!";    
                        }
                        else if (p > 0.04 )
                        {
                                
                            LastResult.Value =  "Nice!";
                        }
                        else
                        {
                            LastResult.Value = "Almost";
                        }
                        LastScore.Value = scoreDelta;
                    }
                }
            }
        }

        var scoreChanged = scoreDelta != 0;
        ScoreChanged.Value = scoreChanged;
        if (scoreChanged)
        {
            Log.Debug($"added score {scoreDelta}", this);
            _score += scoreDelta;
        }


        TotalScore.Value = _score;
    }

    private HashSet<int> _scoredSceneIndices = new();
    private bool _wasPressed;
    private int _score;
        

    [Input(Guid = "D6BD6745-16F7-4DC4-90CE-8CE22D899633")]
    public readonly InputSlot<bool> TriggerReset = new();
        
    [Input(Guid = "EFA3009A-5D0B-4925-BF3A-40C7699721E8")]
    public readonly InputSlot<bool> IsActive = new();

    [Input(Guid = "2FD6DC28-A3F8-4328-83D2-E20E665BBE71")]
    public readonly InputSlot<bool> KeyPressed = new();
        
    [Input(Guid = "163DA70F-3AAA-4321-B575-DFF9F5772B30")]
    public readonly InputSlot<int> SceneIndex = new();

    [Input(Guid = "587A682A-69A1-43B4-8F3E-EE2667A092AC")]
    public readonly InputSlot<int> ScoreForScene = new();

        
    [Input(Guid = "A808906F-EE1B-4D18-B132-B4A11A71905E")]
    public readonly InputSlot<float> PerfectTime = new();

    [Input(Guid = "933950C5-2723-446E-852C-0E0EDF59C9D9")]
    public readonly InputSlot<float> InvalidPressPenalty = new();

    [Input(Guid = "E7672D3A-A5D3-449B-9A6D-1224C0AEAB4F")]
    public readonly InputSlot<float> TimeDifferenceAmplify = new();

}