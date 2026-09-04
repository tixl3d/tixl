using T3.Core.Utils;

namespace T3.Editor.Gui.Interaction.Timing;

public class BpmDetection
{
    public float SampleDurationInSec { get; set; } = 25;
    public int BpmRangeMin { get; set; } = 80;
    public int BpmRangeMax { get; set; } = 180;
    public float NormalizedFrequencyRangeMin { get; set; } = 0;
    public float NormalizedFrequencyRangeMax { get; set; } = 0.2f;
    public float LockInFactor { get; set; } = 0.001f;
    public bool HasSufficientSampleData => _addedSampleCount >= SampleBufferSize;
        
    /// <summary>
    /// This should be called once a frame with a fresh set of fftSamples
    /// </summary>
    public void AddFftSample(float[] fftBuffer)
    {
        BpmRangeMin = BpmRangeMin.Clamp(50, 200);
        BpmRangeMax = BpmRangeMax.Clamp(BpmRangeMin, 200);

        UpdateSampleBuffer(fftBuffer);
    }
        
    /// <summary>
    /// Find the best matching BPM-rate. 
    /// </summary>
    /// <returns>This call is very expensive (expect several milliseconds!)
    /// and should not be called every frame.</returns>
    public float ComputeBpmRate()
    {
        var bpmStepCount = BpmRangeMax - BpmRangeMin;
        if (_bpmEnergies.Count != bpmStepCount)
            _bpmEnergies = new List<float>(new float[bpmStepCount]);

        SmoothBuffer(ref _smoothedSampleBuffer, _sampleBuffer);

        // Find best match in all relevant bpm rates
        var bestBpm = 0f;
        var bestMeasurement = float.PositiveInfinity;

        for (var bpm = BpmRangeMin; bpm < BpmRangeMax; bpm++)
        {
            var m = MeasureEnergyDifference(bpm) / ComputeFocusFactor(bpm, _currentBpm, 4, LockInFactor);
            if (m < bestMeasurement)
            {
                bestMeasurement = m;
                bestBpm = bpm;
            }

            _bpmEnergies[bpm - BpmRangeMin] = m;
        }

        foreach (var offset in _searchOffsets)
        {
            var bpm = _currentBpm + offset;
            if (bpm < 70 || bpm > 170)
                continue;

            var m = MeasureEnergyDifference(bpm) / ComputeFocusFactor(bpm, _currentBpm, 2, 0.01f);
            if (!(m < bestMeasurement))
                continue;
            bestMeasurement = m;
            bestBpm = bpm;
        }
            
        _currentBpm = bestBpm;
        return bestBpm;
    }
        
        
    /// <summary>
    /// Create a fall-off curve   
    /// </summary>
    /// <remarks>https://www.desmos.com/calculator/xgculbggo1</remarks>
    private float ComputeFocusFactor(float value, float targetValue, float range = 3, float amplitude = 0.1f)
    {
        var deviance = Math.Abs(value - targetValue);
        var bump = Math.Max(0, 1 - (1f / (range * range) * deviance * deviance)) * amplitude + 1;
        return Math.Max(bump, 1);
    }

    public static float LastVolume;

    private void UpdateSampleBuffer(IReadOnlyList<float> fftBuffer)
    {
        SampleDurationInSec = SampleDurationInSec.Clamp(1, 60);
        if (_sampleBuffer.Count != SampleBufferSize)
            _sampleBuffer = new List<float>(new float[SampleBufferSize]);

        var lowerBorder = (int)(NormalizedFrequencyRangeMin.Clamp(0, 1) * FftResolution);
        var upperBorder = (int)(NormalizedFrequencyRangeMax.Clamp(0, 1) * FftResolution);
        if (upperBorder <= lowerBorder)
            return;

        _addedSampleCount++;
            
        var sum = 0f;
        for (var index = lowerBorder; index < upperBorder; index++)
        {
            sum += fftBuffer[index];
        }

        //Log.Debug("Added fft sum " + sum);

        LastVolume = sum;
            
        _sampleBuffer.Add(sum);
        if (_sampleBuffer.Count > SampleBufferSize)
            _sampleBuffer.RemoveAt(0);
    }

    /// <summary>
    /// Subtracts the a smoothed version from the the sampleBuffer to cancel variations
    /// in volume / energy.
    /// </summary>
    /// <remarks>This method is very slow (more like a proof-of-concept). It
    /// could be easily optimized by applying the smoothing only to the head sample buffer.</remarks>
    private static void SmoothBuffer(ref float[] cleanedBuffer, IReadOnlyList<float> sampleBuffer)
    {
        if (cleanedBuffer.Length != sampleBuffer.Count)
            cleanedBuffer = new float[sampleBuffer.Count];

        const int smoothSteps = 5;
        if (sampleBuffer.Count < smoothSteps * 2 + 1)
            return;
        for (var i = smoothSteps; i < sampleBuffer.Count - smoothSteps; i++)
        {
            var sum = 0f;
            for (var j = -smoothSteps; j < smoothSteps; j++)
            {
                sum += sampleBuffer[i + j];
            }

            cleanedBuffer[i] = Math.Max(0, sampleBuffer[i] - sum / (smoothSteps * 2 + 1));
        }
    }

    /// <summary>
    /// The actual "algorithm" measure the difference between timing offsets in the sample buffer.
    /// It shifts towards the presence and skips comparision of older parts for which not all
    /// copies can have data. 
    /// </summary>
    /// <remarks>This is slow as f***k and should be easy to optimize.</remarks>
    private float MeasureEnergyDifference(float bpm)
    {
        var dt = (240f / bpm * 60 / 4);
        var sum = 0.1f;

        const int slideScans = 4;
        const int clipStart = (int)(240f / 80 * 60 / 4) * slideScans + 1;

        for (var j = 1; j < slideScans; j++)
        {
            var offset = (int)(dt * j);
            var startIndex = Math.Max(clipStart, offset);
            if (startIndex >= _smoothedSampleBuffer.Length)
                continue;

            for (var i = startIndex; i < _smoothedSampleBuffer.Length; i++)
            {
                sum += Math.Abs(_smoothedSampleBuffer[i] - _smoothedSampleBuffer[i - offset]);
            }
        }

        return sum;
    }

    private int _addedSampleCount;
        
    private const int FramesPerSecond = 60;
    private const int FftResolution = 512;
    private float _currentBpm = 66;
    private readonly float[] _searchOffsets = { -0.3f, -0.1f, 0, 0.1f, 0.3f, };

    private int SampleBufferSize => (int)SampleDurationInSec.Clamp(1, 60) * FramesPerSecond;
        
    private List<float> _sampleBuffer = new();
    private float[] _smoothedSampleBuffer = Array.Empty<float>();
    private List<float> _bpmEnergies = new(128);
}