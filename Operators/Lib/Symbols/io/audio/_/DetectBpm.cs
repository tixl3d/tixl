namespace Lib.io.audio._;

[Guid("e712e801-863d-45c5-9ef8-fbe90dcb8375")]
/// <summary>
/// This is an older implementation. A slighted updated
/// algorithm can be found in <see cref="Interaction.Timing.BpmDetection"/>
/// </summary>
internal sealed class DetectBpm : Instance<DetectBpm>
{
    [Output(Guid = "E907A286-BD65-44B5-ACBB-B880CD192348")]
    public readonly Slot<List<float>> Measurements = new();

    [Output(Guid = "8f3fae32-e216-4596-9067-c56234fb51e6")]
    public readonly Slot<float> DetectedBpm = new();

    public DetectBpm()
    {
        DetectedBpm.UpdateAction += Update;
        Measurements.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var fft = FftInput.GetValue(context);
        if (fft == null || fft.Count == 0)
        {
            return;
        }

        _bpmRangeMin = LowestBpm.GetValue(context);
        if (_bpmRangeMin < 50)
            _bpmRangeMin = 50;
        else if (_bpmRangeMin > 200)
            _bpmRangeMin = 200;

        _bpmRangeMax = HighestBpm.GetValue(context);
        if (_bpmRangeMax < _bpmRangeMin)
            _bpmRangeMax = _bpmRangeMin + 1;
        else if (_bpmRangeMax > 200)
            _bpmRangeMax = 200;

        if (_bpmEnergies.Count != _bpmRangeMax - _bpmRangeMin) 
            _bpmEnergies = new List<float>(new float[_bpmRangeMax-_bpmRangeMin]);

        var bufferDuration = (int)BufferDurationSec.GetValue(context) * 60;

        if (bufferDuration < 60)
        {
            bufferDuration = 60;
        }
        else if (bufferDuration > 60 * 60)
        {
            bufferDuration = 60 * 60;
        }
            
        _bufferLength = bufferDuration;
        _lockInFactor = LockItFactor.GetValue(context);

        UpdateBuffer(fft, LowerLimit.GetValue(context), UpperLimit.GetValue(context));
        SmoothBuffer(ref _smoothedBuffer, _buffer);

        var bestBpm = 0f;
        var bestMeasurement = float.PositiveInfinity;
            

        for(var bpm = _bpmRangeMin; bpm < _bpmRangeMax; bpm++ )
        {
            var m = MeasureEnergyDifference(bpm) / ComputeFocusFactor(bpm, _currentBpm, 4, _lockInFactor);
            if (m < bestMeasurement)
            {
                bestMeasurement = m;
                bestBpm = bpm;
            }

            _bpmEnergies[bpm - _bpmRangeMin] = m;
        }
            
        foreach(var offset in _searchOffsets)
        {
            var bpm = _currentBpm + offset;
            if (bpm < 70 || bpm > 160)
                continue;
                
            var m = MeasureEnergyDifference(bpm) / ComputeFocusFactor(bpm, _currentBpm, 2, 0.01f);
            if (!(m < bestMeasurement))
                continue;
            bestMeasurement = m;
            bestBpm = bpm;
        }
            
        DetectedBpm.Value = bestBpm;
        _currentBpm = bestBpm;
        Measurements.Value = _bpmEnergies;
    }

    private float ComputeFocusFactor(float value, float targetValue, float range=3, float amplitude = 0.1f)
    {
        float deviance = Math.Abs(value - targetValue);
        var bump = Math.Max(0, 1 - (1f / (range * range) * deviance * deviance)) * amplitude + 1;
        return Math.Max(bump,1);
    }

        
    private  int _bpmRangeMin = 65;
    private  int _bpmRangeMax = 150;
    //private  int _bpmRangeSteps = bpmRangeMax - bpmRangeMin;
    private List<float> _bpmEnergies = new(128);
    private float _lockInFactor = 0;
        
    private float _currentBpm = 122;
    private float[] _searchOffsets = new float[]{  -0.5f, -0.1f,  0,  0.1f, 0.5f, };

    private void UpdateBuffer(List<float> fftBuffer, int lowerBorder, int upperBorder)
    {
            
        if(_buffer.Count != _bufferLength)
            _buffer = new List<float>(new float[_bufferLength]);

        if (upperBorder <= lowerBorder)
            return;
            
        if (lowerBorder < 0)
            lowerBorder = 0;

        if (upperBorder > fftBuffer.Count)
            upperBorder = fftBuffer.Count;
            
        var sum = 0f;
        for (var index = lowerBorder; index < upperBorder; index++)
        {
            sum += fftBuffer[index];
        }

        _buffer.Add(sum); 
        if(_buffer.Count > _bufferLength)
            _buffer.RemoveAt(0);
    }

    private void SmoothBuffer(ref float[] output, List<float> buffer)
    {
        if(output.Length != buffer.Count)
            output = new float[buffer.Count];
            

        int smoothSteps = 5;
        if (buffer.Count < smoothSteps * 2 +1)
            return;
        for (int i = smoothSteps; i < buffer.Count - smoothSteps; i++)
        {
            var sum = 0f;
            for (int j =  -smoothSteps; j < smoothSteps; j++)
            {
                sum+= buffer[i+j];
            }
                
            output[i] = Math.Max(0,buffer[i]- sum/(smoothSteps*2+1));
        }

    }

    private float[] _smoothedBuffer = new float[60*60];

    private float MeasureEnergyDifference(float bpm)
    {
        var dt = (240f / bpm * 60/4 );
        var sum = 0.1f;
             
        var slideScans = 4;
        var clipStart = (int)(240f / 80 * 60/4) * slideScans+1;
            
        for (int j = 1; j < slideScans; j++)
        {
            for (int i = Math.Max(clipStart, (int)(dt*j)); i < _smoothedBuffer.Length; i++)
            {
                if (i >= _smoothedBuffer.Length)
                    break;
                    
                sum += Math.Abs(_smoothedBuffer[i] - _smoothedBuffer[i - (int)(dt*j)]);
            }
        }

        return sum;
    }

    private List<float> _buffer = new(60*60);

    [Input(Guid = "3d2d523e-6578-488c-92fa-9e2a3a773e11")]
    public readonly InputSlot<int> LowerLimit = new(0);

    [Input(Guid = "25920975-44fd-4959-be50-b895dd3a6493")]
    public readonly InputSlot<int> UpperLimit = new(0);

    [Input(Guid = "A4763489-0977-4C7E-BACB-89C010D4A12D")]
    public readonly InputSlot<float> BufferDurationSec = new(0);

    [Input(Guid = "D68C056D-7E66-4FE7-8260-7BA43F272DC6")]
    public readonly InputSlot<int> LowestBpm = new(0);

    [Input(Guid = "17208FFF-AB9A-46AA-AB1C-A1AE426CA5DF")]
    public readonly InputSlot<int> HighestBpm = new(0);

    [Input(Guid = "F50CD488-9914-491D-9861-621A9C93019D")]
    public readonly InputSlot<float> LockItFactor = new(0);

        
    [Input(Guid = "7ba20a6a-13f9-47a2-9d46-b8ac59210f08")]
    public readonly InputSlot<List<float>> FftInput = new(new List<float>(20));

    private int _bufferLength;
}