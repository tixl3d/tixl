using Newtonsoft.Json;
using T3.Core.Animation;

// ReSharper disable CompareOfFloatsByEqualityOperator

namespace Lib.io.audio._obsolete;

[Guid("a9e77500-ccb5-45b9-9f17-0d9bf9b58fb5")]
internal sealed class SoundTrackLevels : Instance<SoundTrackLevels>
{
    [Output(Guid = "CFAD7CDE-8E78-4983-924A-0A50F15EF747", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<float> Level = new();

    [Output(Guid = "39EA5257-D6F3-4CCF-B03E-17D98C1E192F", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<float> BeatIndex = new();

    [Output(Guid = "49F89288-3066-4282-8E8D-0828D814A599", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<float> BeatTime = new();

    [Output(Guid = "409D628F-2CBE-4BB1-9937-B78203E68489", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<float> Loudness = new();

    public SoundTrackLevels()
    {
        Level.UpdateAction += Update;
        BeatIndex.UpdateAction += Update;
        Loudness.UpdateAction += Update;
        Level.UpdateAction += Update;
    }
        

    private void Update(EvaluationContext context)
    {
        var needsRecalcAverage = false;

        var filePath = FilePath.GetValue(context);

        if (!TryGetFilePath(filePath, out var absolutePath))
        {
            Log.Error($"Could not find file: {filePath}", this);
            return;
        }

        if (absolutePath != _filepath)
        {
            _filepath = absolutePath;

            using (var reader = new StreamReader(absolutePath))
            {
                var jsonString = reader.ReadToEnd();
                _upLevels = JsonConvert.DeserializeObject<float[]>(jsonString);
                if (_upLevels == null || _upLevels.Length == 0)
                {
                    Log.Warning("Loading sound levels failed", this);
                    return;
                }
            }

            needsRecalcAverage = true;
        }

        var smoothWindow = (int)Clamp(Smooth.GetValue(context), 1, MaxSmoothWindow);

        needsRecalcAverage |= smoothWindow != _smoothWindow;

        if (needsRecalcAverage)
        {
            _minTimeBetweenPeaks = MinTimeBetweenPeaks.GetValue(context);
            _smoothWindow = smoothWindow;
            _maxLevel = 0f;
            _averageLevels = new float[_upLevels.Length];
            _averageLevels2 = new float[_upLevels.Length];

            foreach (var l in _upLevels)
            {
                _maxLevel = Math.Max(l, _maxLevel);
            }

            SmoothBuffer(ref _upLevels, ref _averageLevels2, _smoothWindow, 1);
            SmoothBuffer(ref _averageLevels2, ref _averageLevels, _smoothWindow, 2);
            SmoothBuffer(ref _averageLevels, ref _averageLevels2, _smoothWindow, 4);
            SmoothBuffer(ref _averageLevels2, ref _averageLevels, _smoothWindow, 8);
        }

        bool needsRescanBeats = needsRecalcAverage;

        var threshold = Threshold.GetValue(context);
        needsRescanBeats |= threshold != _threshold;

        var minTimeBetweenPeaks = MinTimeBetweenPeaks.GetValue(context);
        needsRescanBeats |= minTimeBetweenPeaks != _minTimeBetweenPeaks;

        if (needsRescanBeats)
        {
            _threshold = threshold;
            _minTimeBetweenPeaks = minTimeBetweenPeaks;
            UpdateAllBeatNumbers();
        }

        //var index = (int)(Time.GetValue(context) * SampleResolutionPerSecond);
        var index = (int)(Playback.Current.TimeInBars * SampleResolutionPerSecond);
        // Log.Debug("INdex:" + index);
        var needToFindNewBoundaries = (index <= _beatStartIndex || index >= _beatEndIndex);
        needToFindNewBoundaries |= needsRescanBeats;

        if (needToFindNewBoundaries)
        {
            FindBoundarysFromBeatNumbers(index, out _beatStartIndex, out _beatEndIndex);
        }

        //FindBeatBoundaries(index, ref _beatStartIndex, ref _beatEndIndex);

        if (_upLevels == null || index >= _upLevels.Length || index <= 0 || !(_maxLevel > 0.001f))
            return;

        Level.Value = _upLevels[index];
            
        // Peaks...
        // BeatTime.Value = (float)Math.Max(0, _uplevels[index] - _averageLevels[index]);                    
            
        // Flashes:
        var t = ((float)index - _beatStartIndex) / SampleResolutionPerSecond;
        BeatTime.Value = (float)Math.Pow(2.71f, -FlashDecay.GetValue(context) * t);
            
        // FlashesWithIntensity...
        // var t2 = ((float)index - _beatStartIndex) / (float)SAMPLE_RESOLUTION_PER_SECOND;
        // BeatTime.Value =  (float)Math.Pow(2.71f, -FlashDecay* t2) * _uplevels[_beatStartIndex];
            
        // TimeSincePeak...
        // BeatTime.Value = ((float)index - _beatStartIndex) / (float)SAMPLE_RESOLUTION_PER_SECOND;

        // TimeToPeak...
        // BeatTime.Value = (_beatEndIndex  - (float)index) / (float)SAMPLE_RESOLUTION_PER_SECOND;                    

            
        // BeatIndex.Value = (float)_beatStartIndex;
        BeatIndex.Value = _beatNumbers[index];
        Loudness.Value = _averageLevels[index];
    }

    private void SmoothBuffer(ref float[] inBuffer, ref float[] outBuffer, int sampleCount, int stepWidth)
    {
        for (var i = 0; i < inBuffer.Length; i++)
        {
            var average = 0f;
            var count = 0f;

            for (var ds = 0; ds < sampleCount; ds++)
            {
                var smoothI = i + (-sampleCount / 2 + ds) * stepWidth;

                if (smoothI < 0 || smoothI >= inBuffer.Length)
                    continue;

                average += inBuffer[smoothI];
                count++;
            }

            outBuffer[i] = average / count;
        }
    }

    private static T Clamp<T>(T val, T min, T max) where T : IComparable<T>
    {
        if (val.CompareTo(min) < 0) return min;
        return val.CompareTo(max) > 0 ? max : val;
    }


    private void UpdateAllBeatNumbers()
    {
        _beatNumbers = new int[_upLevels.Length];
        var lastUpperIndex = 0;
        var lastLowerIndex = 0;
        var lastBeatStartIndex = 0;
        int beatsFound = 0;

        for (int i = 0; i < _upLevels.Length; i++)
        {
            if (i >= lastUpperIndex)
            {
                FindBeatBoundaries(i, ref lastLowerIndex, ref lastUpperIndex);
                if (lastUpperIndex > lastBeatStartIndex + _minTimeBetweenPeaks * SampleResolutionPerSecond)
                {
                    beatsFound++;
                    lastBeatStartIndex = lastUpperIndex;
                }
            }

            _beatNumbers[i] = beatsFound;
        }
    }

    private bool IsIndexValid(int timeIndex)
    {
        if (timeIndex < 0 || timeIndex >= _upLevels.Length)
            return false;
        return true;
    }

    /** Use the beat index array to find boundaries.
        We can't use the original method, because MinTimeBetween peaks would be too complicated to implement
    */
    private void FindBoundarysFromBeatNumbers(int timeIndex, out int leftBoundaryIndex, out int rightBoundaryIndex)
    {
        leftBoundaryIndex = timeIndex;
        rightBoundaryIndex = timeIndex;
        if (!IsIndexValid(timeIndex)) return;

        var currentBeatNumber = _beatNumbers[timeIndex];

        while (leftBoundaryIndex > 0 && _beatNumbers[leftBoundaryIndex - 1] == currentBeatNumber)
        {
            leftBoundaryIndex--;
        }

        while (rightBoundaryIndex < _beatNumbers.Length - 1 && _beatNumbers[rightBoundaryIndex + 1] == currentBeatNumber)
        {
            rightBoundaryIndex++;
        }
    }

    private void FindBeatBoundaries(int timeIndex, ref int leftBoundaryIndex, ref int rightBoundaryIndex)
    {
        if (!IsIndexValid(timeIndex)) return;

        const int maxSteps = 12 * SampleResolutionPerSecond;

        // Find left boundary
            
        var wasAboveThreshold = false;
        var maxLevel = 0f;
        int stepIndex;

        for (stepIndex = 0; stepIndex < maxSteps && (timeIndex - stepIndex) >= 0; stepIndex++)
        {
            leftBoundaryIndex = timeIndex - stepIndex;
            var level = _upLevels[leftBoundaryIndex] - _averageLevels[leftBoundaryIndex];
            maxLevel = Math.Max(maxLevel, level);
            var isAboveThreshold = level > _threshold;
            var isFlank = (stepIndex > 0 && wasAboveThreshold && !isAboveThreshold);

            if (isFlank)
            {
                break;
            }

            wasAboveThreshold = isAboveThreshold;
        }

        // Find right boundary            
        for (stepIndex = 0; stepIndex < maxSteps && (timeIndex + stepIndex) < _averageLevels.Length; stepIndex++)
        {
            rightBoundaryIndex = timeIndex + stepIndex;
            var level = _upLevels[rightBoundaryIndex] - _averageLevels[rightBoundaryIndex];
            var isAboveThreshold = level > _threshold;
            var isFlank = (stepIndex > 0 && !wasAboveThreshold && isAboveThreshold);

            if (isFlank)
            {
                break;
            }

            wasAboveThreshold = isAboveThreshold;
        }
    }

    private const int MaxSmoothWindow = 500;

    private const int SampleResolutionPerSecond = 100;
    private string _filepath = "";
    private float[] _upLevels = new float[0];
    private int _smoothWindow = -1;

    private float[] _averageLevels = new float[0];
    private float[] _averageLevels2 = new float[0];
    private int[] _beatNumbers = new int[0];

    private float _maxLevel;
    private int _beatStartIndex;
    private int _beatEndIndex;
    private float _threshold;
    private float _minTimeBetweenPeaks = 0.05f;

    //>>> _params
    [Input(Guid = "839BAB2B-440F-40E9-A670-723F2A684AA9")]
    public readonly InputSlot<string> FilePath = new(".");

    // [Input(Guid = "FE2812F5-1C3F-4560-B6E7-55F0E895AEB2")]
    // public readonly InputSlot<float> Time = new InputSlot<float>();

    [Input(Guid = "4EF9500A-DAA4-4979-A03C-306E8E56C2F1")]
    public readonly InputSlot<float> Threshold = new();

    [Input(Guid = "CA11288E-E040-4ED6-BCF4-402B7214861A")]
    public readonly InputSlot<float> Smooth = new();

    [Input(Guid = "B51EF02F-C91B-4FA4-B8A1-2B660CEF5F5E")]
    public readonly InputSlot<int> BeatTimeMode = new();

    [Input(Guid = "9B510876-F2A4-49F1-ACD2-8CEFB5662A2A")]
    public readonly InputSlot<float> FlashDecay = new();

    [Input(Guid = "AE5D1534-7A35-4148-88BD-DB2E353D5FC1")]
    public readonly InputSlot<float> MinTimeBetweenPeaks = new();

}