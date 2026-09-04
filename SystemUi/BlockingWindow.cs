using T3.SystemUi;

namespace T3.Core.SystemUi;

public static class BlockingWindow
{
    private static IMessageBoxProvider _instance = null!;
    public static IMessageBoxProvider Instance
    {
        get => _instance;
        set
        {
            if (_instance != null)
                throw new Exception($"{typeof(BlockingWindow)}'s {nameof(Instance)} already set to {_instance.GetType()}");
            
            _instance = value;
        }
    }
}