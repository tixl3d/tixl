namespace SilkWindows;

public static class ImGuiWindowService
{
    private static IImguiWindowProvider? _instance;
    
    public static IImguiWindowProvider Instance
    {
        get => _instance!;
        set
        {
            if (_instance != null)
                throw new Exception($"{typeof(ImGuiWindowService)}'s {nameof(Instance)} already set to {_instance.GetType()}");
            
            _instance = value;
        }
    }
}

public readonly record struct FontPack(TtfFont Regular, TtfFont Bold, TtfFont Small, TtfFont Large);
public readonly record struct TtfFont(string Path, float PixelSize);