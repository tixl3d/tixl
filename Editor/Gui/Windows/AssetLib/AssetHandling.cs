#nullable enable

using T3.Core.Resource.Assets;
using T3.Editor.Gui.Styling;

namespace T3.Editor.Gui.Windows.AssetLib;

/// <summary>
/// Binds <see cref="AssetType"/> with file extensions for actual UI use in editor
/// </summary>
internal static class AssetHandling
{
    public static AssetType Images = new AssetType("Image", [
                                             FileExtensionRegistry.GetUniqueId("png"),
                                             FileExtensionRegistry.GetUniqueId("jpg"),
                                             FileExtensionRegistry.GetUniqueId("jpeg"),
                                             FileExtensionRegistry.GetUniqueId("bmp"),
                                             FileExtensionRegistry.GetUniqueId("tga"),
                                             FileExtensionRegistry.GetUniqueId("gif"),
                                             FileExtensionRegistry.GetUniqueId("dds"),
                                         ])
                                         {
                                             PrimaryOperators = [new Guid("0b3436db-e283-436e-ba85-2f3a1de76a9d")], // Load Image
                                             Color = UiColors.ColorForTextures,
                                             IconId = (uint)Icon.FileImage,
                                             Subfolders = ["images", "image"],
                                         };
    
    public static void InitAssetTypes()
    {
        AssetType.RegisterType(new AssetType("Obj", [
                                       FileExtensionRegistry.GetUniqueId("obj")
                                   ])
                                   {
                                       PrimaryOperators = [new Guid("be52b670-9749-4c0d-89f0-d8b101395227")], // LoadObj
                                       Color = UiColors.ColorForGpuData,
                                       IconId = (uint)Icon.FileGeometry,
                                       Subfolders = ["geometry","mesh","meshes","objs"],
                                   });

        AssetType.RegisterType(new AssetType("Gltf", [
                                       FileExtensionRegistry.GetUniqueId("glb"),
                                       FileExtensionRegistry.GetUniqueId("gltf"),
                                   ])
                                   {
                                       PrimaryOperators =
                                           [
                                               new Guid("00618c91-f39a-44ea-b9d8-175c996460dc"), // LoadGltfScene
                                               new Guid("92b18d2b-1022-488f-ab8e-a4dcca346a23"), // LoadGltf
                                               // TODO: add more
                                           ],
                                       Color = UiColors.ColorForGpuData,
                                       IconId = (uint)Icon.FileGeometry,
                                       Subfolders = ["geometry","mesh","meshes","gltf"],
                                   });

        AssetType.RegisterType(Images);
        AssetType.RegisterType(new AssetType("Video", [
                                       FileExtensionRegistry.GetUniqueId("mp4"),
                                       FileExtensionRegistry.GetUniqueId("mov"),
                                       FileExtensionRegistry.GetUniqueId("mpg"),
                                       FileExtensionRegistry.GetUniqueId("mpeg"),
                                       FileExtensionRegistry.GetUniqueId("m4v"),
                                   ])
                                   {
                                       PrimaryOperators = [new Guid("914fb032-d7eb-414b-9e09-2bdd7049e049")], // PlayVideo
                                       Color = UiColors.ColorForTextures,
                                       IconId = (uint)Icon.FileVideo,
                                       Subfolders = ["videos", "video", "media"],
                                   });
        AssetType.RegisterType(new AssetType("Audio", [
                                       FileExtensionRegistry.GetUniqueId("wav"),
                                       FileExtensionRegistry.GetUniqueId("mp3"),
                                       FileExtensionRegistry.GetUniqueId("ogg"),
                                   ])
                                   {
                                       PrimaryOperators =
                                               [new Guid("c2b2758a-5b3e-465a-87b7-c6a13d3fba48")], // PlayAudioClip
                                       Color = UiColors.ColorForValues,
                                       IconId = (uint)Icon.FileAudio,
                                       Subfolders = ["audio", "soundtrack","samples"],
                                       
                                   });
        AssetType.RegisterType(new AssetType("Shader", [
                                       FileExtensionRegistry.GetUniqueId("hlsl")
                                   ])
                                   {
                                       PrimaryOperators =
                                           [
                                               new Guid("a256d70f-adb3-481d-a926-caf35bd3e64c"), // ComputeShader
                                               new Guid("646f5988-0a76-4996-a538-ba48054fd0ad"), // VertexShader
                                               new Guid("f7c625da-fede-4993-976c-e259e0ee4985"), // PixelShader
                                           ],
                                       Color = UiColors.ColorForString,
                                       IconId = (uint)Icon.FileShader,
                                       Subfolders = ["shaders"],
                                   });
        AssetType.RegisterType(new AssetType("JSON",
                                   [
                                       FileExtensionRegistry.GetUniqueId("json")
                                   ])
                                   {
                                       PrimaryOperators =
                                           [
                                               new
                                                   Guid("5f71d2f8-98c8-4502-8f40-2ea4a1e18cca"), // ReadFile
                                           ],
                                       Color = UiColors.ColorForString,
                                       IconId = (uint)Icon.FileDocument,
                                       Subfolders = ["json", "data"],
                                   });
        AssetType.RegisterType(new AssetType("TiXLFont",
                                   [
                                       FileExtensionRegistry.GetUniqueId("fnt")
                                   ])
                                   {
                                       PrimaryOperators =
                                           [
                                               new
                                                   Guid("fd31d208-12fe-46bf-bfa3-101211f8f497"), // Text
                                           ],
                                       Color = UiColors.ColorForCommands,
                                       IconId = (uint)Icon.FileT3Font,
                                       Subfolders = ["fonts", "font"],
                                   });
        AssetType.RegisterType(new AssetType("Svg",
                                   [
                                       FileExtensionRegistry
                                          .GetUniqueId("svg")
                                   ])
                                   {
                                       PrimaryOperators =
                                           [    
                                               new Guid("d05739d3-f89d-488d-85d0-c0d115265b75"), // LoadSvgAsTexture2D
                                               new Guid("e8d94dd7-eb54-42fe-a7b1-b43543dd457e"), // LoadSvg
                                           ],
                                       Color = UiColors.ColorForValues,
                                       IconId = (uint)Icon.FileVector,
                                       Subfolders = ["svg"],
                                   });
        AssetType.RegisterType(new AssetType("Text",
                                   [
                                       FileExtensionRegistry
                                          .GetUniqueId("txt")
                                   ])
                                   {
                                       PrimaryOperators =
                                           [
                                               new
                                                   Guid("5f71d2f8-98c8-4502-8f40-2ea4a1e18cca"), // ReadFile
                                           ],
                                       Color = UiColors
                                          .ColorForString,
                                       IconId = (uint)Icon
                                          .FileDocument,
                                       Subfolders = ["text","data"],
                                   });
    }

    internal static int TotalAssetCount = 0;
}