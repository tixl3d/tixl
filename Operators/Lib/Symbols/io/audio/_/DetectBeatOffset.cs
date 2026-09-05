using T3.Core.Animation;
using T3.Core.Utils;

namespace Lib.io.audio._;

[Guid("1fa651c8-ab73-4ca0-9506-84602bbf2fcb")]
/// <summary>
/// This is an older implementation. A slighted updated
/// algorithm can be found in <see cref="Interaction.Timing.BpmDetection"/>
/// </summary>
internal sealed class DetectBeatOffset : Instance<DetectBeatOffset>
{
    [Output(Guid = "c53fb442-4dd7-473c-8a1f-1adaabe3bcf7")]
    public readonly Slot<List<float>> Measurements = new();
        
    [Output(Guid = "68794F45-9537-4E88-91D7-6C4E93205BBC")]
    public readonly Slot<float> Peak = new();
        
    [Output(Guid = "819469C8-7178-4712-9EB5-7BC7AEFA32D8")]
    public readonly Slot<float> Confidence = new();


    public DetectBeatOffset()
    {
        Measurements.UpdateAction += Update;
        Peak.UpdateAction += Update;
    }
        
    private class TimeEntry
    {
        public double Time;
        public float[] FftValues;
    }

    private double _lastEvalTime;


    //private List<float[]> _slicesForSmoothing = new List<float[]>();
        
    private void Update(EvaluationContext context)
    {
        var inputFft = FftInput.GetValue(context);
        if (inputFft == null || inputFft.Count == 0)
        {
            return;
        }

        var timeSinceLastEval = Playback.RunTimeInSecs - _lastEvalTime;
        if (timeSinceLastEval < 0.01)
            return;
            
        _lastEvalTime = Playback.RunTimeInSecs;

        const float assumedFrameRate = 60f;
        //var timeSinceLastUpdate = Playback.LastFrameDuration;
        //var fillCount =  (int)Math.Round(timeSinceLastUpdate / (1 / assumedFrameRate) - 0.5f) + 1; 
            
        var historyDurationInSecs = 20f;
        var maxFftHistoryLength =  (int)(assumedFrameRate * historyDurationInSecs);

        var currentTime = context.Playback.TimeInSecs;
        var maxValueCount = 8;

        if (inputFft.Count < maxValueCount)
        {
            return;
        }
            
            
            
        // Down sample
        var values = new float[maxValueCount];
        var delta = (inputFft.Count / (float)maxValueCount);
        var sourceIndex = 0f;
        for (var valueIndex = 0; valueIndex < maxValueCount && sourceIndex <= inputFft.Count; valueIndex++)
        {
            values[valueIndex] = inputFft[(int)sourceIndex];
            sourceIndex += delta;
        }

        // const int smoothWindow = 3;
        // _slicesForSmoothing.Insert(0, values);
        // if(_slicesForSmoothing.Count >= smoothWindow)
        //     _slicesForSmoothing.RemoveRange(smoothWindow , _slicesForSmoothing.Count - smoothWindow );
            
        // Smooth
        // var smoothedValues = new float[maxValueCount];
        // for (var valueIndex = 0; valueIndex < maxValueCount; valueIndex++)
        // {
        //     var s = 0f;
        //     for (var smoothIndex = 0; smoothIndex < _slicesForSmoothing.Count; smoothIndex++)
        //     {
        //         s += _slicesForSmoothing[smoothIndex][valueIndex];
        //     }
        //     smoothedValues[valueIndex] = s / _slicesForSmoothing.Count;
        // }
            
        _fftHistory.Insert(0, new TimeEntry()
                                  {
                                      Time= currentTime,
                                      FftValues= values,
                                  });
            
        // Clamp history to length of buffer            
        if(_fftHistory.Count >= maxFftHistoryLength)
            _fftHistory.RemoveRange(maxFftHistoryLength , _fftHistory.Count - maxFftHistoryLength);

            
        //Log.Debug($"Slice count {sliceIndexForBar} {durationOfMeasure:0.00} {context.Playback.Bpm}", this);
        var shiftCount = 25;

        // Swipe scan 
        _results.Clear();
        var min = float.PositiveInfinity;
        var max = float.NegativeInfinity;
        var maxIndex = 0;
        var maxValue = float.NegativeInfinity;
            
        var durationOfMeasure = context.Playback.SecondsFromBars(1);
        var sliceIndexForBar = (int)(durationOfMeasure * assumedFrameRate + 0.5f);
        var sliceLength = sliceIndexForBar * 4;
        //Log.Debug($"her length historyLenght: {maxFftHistoryLength} offset:{sliceIndexForBar}  bpm:{context.Playback.Bpm:0.0}  bar: {durationOfMeasure:0.00}s  lenght:{sliceLength}",this);
            
        for (int index = 0 ; index < shiftCount; index++)
            //for (var shiftIndex = - shiftCount / 2; shiftIndex < shiftCount /2; shiftIndex++)
        {
            var shiftIndex = index - shiftCount / 2;
            var energyDifference = GetEnergyDifference(shiftIndex + sliceIndexForBar, sliceLength);
            min = MathF.Min(energyDifference, min);
            max = MathF.Max(energyDifference, max);
            _results.Add(energyDifference );
            if (energyDifference > maxValue)
            {
                //maxIndex = index;
                maxValue = energyDifference;
            }
        }

        // Remap values
        var totalSum = 0f;
        var sum = 0f;
        for (var index = 0; index < _results.Count; index++)
        {
            var remappedValue = MathF.Pow(MathUtils.RemapAndClamp(_results[index], min, max, 0,1),2);
            totalSum += remappedValue * index;
            sum += remappedValue;
            _results[index] = remappedValue;
        }

        var meanIndex = totalSum / sum - (shiftCount /2f);

        Confidence.Value = (10 / (MathF.Abs(meanIndex - maxIndex) + 0.001f)).Clamp(0, 1);
            
            
        Peak.Value = meanIndex;
        Measurements.Value = _results;
            
        Peak.DirtyFlag.Clear();
        Measurements.DirtyFlag.Clear();
    }

    private List<float> _results = new();

    private float GetEnergyDifference(int offset, int sliceLength)
    {
        float energyDifference = 0;

        for (var sliceIndex = 0; sliceIndex < sliceLength; sliceIndex++)
        {
            if (sliceIndex >= _fftHistory.Count)
            {
                continue;
            }
                
            int shiftedIndex = sliceIndex + offset;
            if (shiftedIndex < 0 || shiftedIndex >= _fftHistory.Count)
            {
                continue;
            }

            var valuesNew = _fftHistory[sliceIndex].FftValues;
            var valuesOld = _fftHistory[shiftedIndex].FftValues;
            var sliceWidth = Math.Min(valuesNew.Length, valuesOld.Length);
            if (sliceWidth == 0)
            {
                continue;
            }

            for (var valueIndex = 0; valueIndex < sliceWidth; valueIndex++)
            {
                var diff = MathF.Abs(valuesNew[valueIndex] - valuesOld[valueIndex]);
                //var energy = 1 / (diff + 0.1f);
                var energy = MathF.Pow(diff, 2); 
                energyDifference += energy;
                //energyDifference = MathF.Pow() * 100,2f) / sliceWidth;
            }
        }

        return 1/ energyDifference;
    }
        
    private readonly List<TimeEntry> _fftHistory = new();
        
    [Input(Guid = "09628f30-9148-477e-a375-a7e661952ee5")]
    public readonly InputSlot<List<float>> FftInput = new(new List<float>(20));
}