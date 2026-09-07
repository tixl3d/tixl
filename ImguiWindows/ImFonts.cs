using ImGuiNET;

namespace SilkWindows;

// ReSharper disable once SuggestBaseTypeForParameterInConstructor
public sealed class ImFonts(ImFontPtr[] fonts)
{
    public readonly bool HasFonts = fonts.Length > 3;
    public ImFontPtr Small => HasFonts ? fonts[0] : ImGui.GetIO().Fonts.Fonts[0];
    public ImFontPtr Regular => HasFonts ? fonts[1] : ImGui.GetIO().Fonts.Fonts[0];
    public ImFontPtr Bold => HasFonts ? fonts[2] : ImGui.GetIO().Fonts.Fonts[0];
    public ImFontPtr Large => HasFonts ? fonts[3] : ImGui.GetIO().Fonts.Fonts[0];
}

public interface IImguiWindowProvider
{
    public object ContextLock { get; }
    public void SetFonts(FontPack fontPack);
    public TData? Show<TData>(string title, IImguiDrawer<TData> drawer, in SimpleWindowOptions? options = null);
    public Task ShowAsync(string title, IImguiDrawer drawer, SimpleWindowOptions? options = null);
    public Task ShowAsync<TData>(string title, AsyncImguiDrawer<TData> drawer, Action<TData> assign, SimpleWindowOptions? options = null);
}