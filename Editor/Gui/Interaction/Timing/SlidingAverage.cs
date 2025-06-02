using T3.Core.Utils;

namespace T3.Editor.Gui.Interaction.Timing;

/// <summary>
/// A helper that implements a queue to smooth values.
/// </summary>
internal sealed class SlidingAverage
{
    public SlidingAverage(int maxMaxLength)
    {
        _maxLength = maxMaxLength;
        _queue = new Queue<double>(maxMaxLength);
    }

    public void Clear(int maxLength)
    {
        _maxLength = maxLength.Clamp(0,100);
        _queue.Clear();
        _currentSum = 0;
    }
    
    public  double UpdateAndCompute(double current)
    {
        if (_maxLength == 0)
            return current;
        
        _queue.Enqueue(current);
        _currentSum += current;

        if (_queue.Count > _maxLength)
        {
            var oldestValue = _queue.Dequeue();
            _currentSum -= oldestValue;
        }

        var averageStrength = 0.0;
        if (_queue.Count > 0)
        {
            averageStrength = _currentSum / _queue.Count;
        }

        return averageStrength;
    }

    private int _maxLength;
    private readonly Queue<double> _queue;
    private double _currentSum;
}