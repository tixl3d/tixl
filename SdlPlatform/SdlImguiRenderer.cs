using System.Runtime.CompilerServices;
using ImGuiNET;
using SDL;
using static SDL.SDL3;

namespace T3.SdlPlatform;

/// <summary>
/// Draws ImGui with SDL's 2D renderer, the job <c>imgui_impl_sdlrenderer3</c> does. Needs no GPU device of its
/// own, so a window that shows before the editor's renderer exists can still draw ImGui.
/// </summary>
internal static unsafe class SdlImguiRenderer
{
    /// <summary>
    /// Uploads the current context's font atlas. With <paramref name="assignToAtlas"/> false the atlas keeps its
    /// texture id, which a shared atlas needs: it belongs to the window that owns it, and <see cref="Render"/>
    /// swaps it for this texture instead.
    /// </summary>
    public static SDL_Texture* CreateFontTexture(SDL_Renderer* renderer, bool assignToAtlas)
    {
        var fonts = ImGui.GetIO().Fonts;
        fonts.GetTexDataAsRGBA32(out IntPtr pixels, out var width, out var height, out _);

        // RGBA bytes in memory are ABGR8888 on a little-endian machine.
        var texture = SDL_CreateTexture(renderer, SDL_PixelFormat.SDL_PIXELFORMAT_ABGR8888, SDL_TextureAccess.SDL_TEXTUREACCESS_STATIC,
                                        width, height);
        if (texture == null)
            return null;

        SDL_UpdateTexture(texture, null, pixels, width * 4);
        SDL_SetTextureBlendMode(texture, SDL_BlendMode.SDL_BLENDMODE_BLEND);
        SDL_SetTextureScaleMode(texture, SDL_ScaleMode.SDL_SCALEMODE_LINEAR);
        if (assignToAtlas)
            fonts.SetTexID((IntPtr)texture);

        return texture;
    }

    /// <param name="sharedAtlasTexture">The texture id a shared font atlas carries; draws using it get <paramref name="fontTexture"/>.</param>
    public static void Render(SDL_Renderer* renderer, ImDrawDataPtr drawData, IntPtr sharedAtlasTexture, SDL_Texture* fontTexture)
    {
        var displayPos = drawData.DisplayPos;
        var stride = Unsafe.SizeOf<ImDrawVert>();

        for (var listIndex = 0; listIndex < drawData.CmdListsCount; listIndex++)
        {
            var cmdList = drawData.CmdLists[listIndex];
            var vertices = (ImDrawVert*)cmdList.VtxBuffer.Data;
            var indices = (ushort*)cmdList.IdxBuffer.Data;
            var vertexCount = cmdList.VtxBuffer.Size;

            // SDL3 takes float colours where ImGui packs bytes, so each list's colours are converted once.
            if (_colors.Length < vertexCount)
                _colors = new SDL_FColor[Math.Max(vertexCount, _colors.Length * 2)];

            for (var i = 0; i < vertexCount; i++)
            {
                var packed = vertices[i].col;
                _colors[i] = new SDL_FColor
                                 {
                                     r = (packed & 0xff) / 255f,
                                     g = ((packed >> 8) & 0xff) / 255f,
                                     b = ((packed >> 16) & 0xff) / 255f,
                                     a = (packed >> 24) / 255f,
                                 };
            }

            fixed (SDL_FColor* colors = _colors)
            {
                for (var cmdIndex = 0; cmdIndex < cmdList.CmdBuffer.Size; cmdIndex++)
                {
                    var cmd = cmdList.CmdBuffer[cmdIndex];
                    if (cmd.UserCallback != IntPtr.Zero)
                        continue;

                    var clip = new SDL_Rect
                                   {
                                       x = (int)(cmd.ClipRect.X - displayPos.X),
                                       y = (int)(cmd.ClipRect.Y - displayPos.Y),
                                       w = (int)(cmd.ClipRect.Z - cmd.ClipRect.X),
                                       h = (int)(cmd.ClipRect.W - cmd.ClipRect.Y),
                                   };
                    if (clip.w <= 0 || clip.h <= 0)
                        continue;

                    SDL_SetRenderClipRect(renderer, &clip);

                    var first = vertices + cmd.VtxOffset;
                    var texture = cmd.TextureId == sharedAtlasTexture && sharedAtlasTexture != IntPtr.Zero
                                      ? fontTexture
                                      : (SDL_Texture*)cmd.TextureId;
                    SDL_RenderGeometryRaw(renderer, texture,
                                          (float*)&first->pos, stride,
                                          colors + cmd.VtxOffset, sizeof(SDL_FColor),
                                          (float*)&first->uv, stride,
                                          vertexCount - (int)cmd.VtxOffset,
                                          (IntPtr)(indices + cmd.IdxOffset), (int)cmd.ElemCount, sizeof(ushort));
                }
            }
        }

        SDL_SetRenderClipRect(renderer, null);
    }

    private static SDL_FColor[] _colors = new SDL_FColor[4096];
}
