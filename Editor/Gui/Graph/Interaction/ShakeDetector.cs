using ImGuiNET;

namespace T3.Editor.Gui.Interaction;

internal sealed class ShakeDetector
{
    public bool TestDragForShake(Vector2 mousePosition)
    {
        var delta = mousePosition - _lastPosition;
        _lastPosition = mousePosition;

        var dx = 0;
        if (Math.Abs(delta.X) > 2)
        {
            dx = delta.X > 0 ? 1 : -1;
        }

        Directions.Add(dx);

        if (Directions.Count < 2)
            return false;

        // Queue length is optimized for 60 fps adjust length for different frame rates
        var queueLength = (int)(QueueLength * (ImGui.GetIO().Framerate / 60f));
        if (Directions.Count > queueLength)
            Directions.RemoveAt(0);

        // Count direction changes
        var changeDirectionCount = 0;

        var lastD = 0;
        var lastRealD = 0;
        foreach (var d in Directions)
        {
            if (lastD != 0 && d != 0)
            {
                if (d != lastRealD)
                {
                    changeDirectionCount++;
                }

                lastRealD = d;
            }

            lastD = d;
        }
                
        var wasShaking = changeDirectionCount >= ChangeDirectionThreshold;
        if (wasShaking)
            ResetShaking();

        return wasShaking;
    }

    public void ResetShaking()
    {
        Directions.Clear();
    }

    private Vector2 _lastPosition = Vector2.Zero;
    private const int QueueLength = 35;
    private const int ChangeDirectionThreshold = 5;
    private readonly List<int> Directions = new(QueueLength);
}