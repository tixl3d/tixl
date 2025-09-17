using Operators.Utils;
using T3.Core.Utils;


namespace Examples.user.pixtur.research.hitform;

[Guid("73faed85-f936-4f54-bef5-600a11405af3")]
public class LoadHFCS : Instance<LoadHFCS>
{
    [Output(Guid = "709BF59B-725C-46DE-9139-A159EFA59688", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly TimeClipSlot<Vector3> Position = new();
        
    [Output(Guid = "B122784E-9787-4B29-A492-AF8EA2DC040D", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Vector3> Rotation = new();
        
    public LoadHFCS()
    {
        Position.UpdateAction = Update;
        Rotation.UpdateAction = Update;
    }
        
    private void Update(EvaluationContext context)
    {
        var wasTriggered = MathUtils.WasTriggered(TriggerReload.GetValue(context), ref _triggered);
        if (Path.DirtyFlag.IsDirty || wasTriggered)
        {
            var filePath = Path.GetValue(context);
            TriggerReload.SetTypedInputValue(false);
                
            if (HitFilmComposite.HitFilm.Load(filePath, out var orderedKeys) && orderedKeys.Count > 0)
            {
                Log.Debug($"Reload: {filePath} with {orderedKeys.Count} keyframes");
                _orderedKeys = orderedKeys;
                Position.TimeClip.TimeRange.Start = (float)context.Playback.BarsFromSeconds( orderedKeys[0].TimeInSeconds);
                Position.TimeClip.SourceRange.Start = (float)context.Playback.BarsFromSeconds( orderedKeys[0].TimeInSeconds);
                Position.TimeClip.TimeRange.End =  (float)context.Playback.BarsFromSeconds(orderedKeys[^1].TimeInSeconds);
                Position.TimeClip.SourceRange.End =  (float)context.Playback.BarsFromSeconds(orderedKeys[^1].TimeInSeconds);
            }
            else
            {
                _orderedKeys.Clear();
            }
        }
            
        if(_orderedKeys==null || _orderedKeys.Count==0)
            return;
            
        //Log.Debug($"  FxTime: {context.LocalFxTime:0.00}  {context.LocalTime:0.00}", this);
        var timeInBars = context.LocalTime;
        var timeInSecs = context.Playback.SecondsFromBars(timeInBars);

        var indexAtTime = MathUtils.FindIndexForTime(_orderedKeys, timeInSecs, i => _orderedKeys[i].TimeInSeconds );
        var keyA = _orderedKeys[indexAtTime];
        if(indexAtTime < _orderedKeys.Count-1)
        {
            var next = _orderedKeys[indexAtTime+1];
            var t = (timeInSecs - keyA.TimeInSeconds) / (next.TimeInSeconds - keyA.TimeInSeconds);
            Position.Value = Vector3.Lerp(keyA.Position, next.Position, (float)t);
            
            Rotation.Value = new Vector3(MathUtils.LerpDegreesAngle(keyA.Orientation.X, next.Orientation.X, (float)t),
                                         MathUtils.LerpDegreesAngle(keyA.Orientation.Y, next.Orientation.Y, (float)t),
                                         MathUtils.LerpDegreesAngle(keyA.Orientation.Z, next.Orientation.Z, (float)t));
        }
        else
        {
            Position.Value = keyA.Position;
            Rotation.Value = keyA.Orientation;
        }
    }
        
    private bool _triggered;
    private List<HitFilmComposite.TransformationKey> _orderedKeys = new();
        
    [Input(Guid = "83b7ea0e-2ab6-4b5e-9706-0b702569c847")]
    public readonly InputSlot<string> Path = new();
        
    [Input(Guid = "81E8AD44-56D6-4944-B81C-A36D035A502C")]
    public readonly InputSlot<bool> TriggerReload = new();
        
}