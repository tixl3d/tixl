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
                                             FileExtensionRegistry.GetUniqueId("tiff"),
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
        // Extensions match what the bundled FFmpeg build (LGPL, 7.x) demuxes and decodes:
        // H.264/HEVC/AV1/VP8/VP9/MPEG-2/4/VC-1 plus the editing codecs (ProRes, DNxHD, HAP, CineForm, FFV1).
        AssetType.RegisterType(new AssetType("Video", [
                                       FileExtensionRegistry.GetUniqueId("mp4"),
                                       FileExtensionRegistry.GetUniqueId("mov"),
                                       FileExtensionRegistry.GetUniqueId("mpg"),
                                       FileExtensionRegistry.GetUniqueId("mpeg"),
                                       FileExtensionRegistry.GetUniqueId("m4v"),
                                       FileExtensionRegistry.GetUniqueId("mkv"),
                                       FileExtensionRegistry.GetUniqueId("avi"),
                                       FileExtensionRegistry.GetUniqueId("webm"),
                                       FileExtensionRegistry.GetUniqueId("wmv"),
                                       FileExtensionRegistry.GetUniqueId("flv"),
                                       FileExtensionRegistry.GetUniqueId("ts"),
                                       FileExtensionRegistry.GetUniqueId("m2ts"),
                                       FileExtensionRegistry.GetUniqueId("mts"),
                                       FileExtensionRegistry.GetUniqueId("mxf"),
                                       FileExtensionRegistry.GetUniqueId("ogv"),
                                   ])
                                   {
                                       PrimaryOperators = [new Guid("914fb032-d7eb-414b-9e09-2bdd7049e049")], // PlayVideo
                                       TimelineClipOperator = new Guid("04c1a6dc-3042-48a8-81d2-0a5a162016dc"), // VideoClip
                                       Color = UiColors.ColorForTextures,
                                       IconId = (uint)Icon.FileVideo,
                                       Subfolders = ["videos", "video", "media"],
                                   });
        // Audio plays through BASS (not FFmpeg): wav/mp3/ogg/aiff natively, flac via the bundled
        // bassflac plugin, aac/m4a/wma through the Media Foundation fallback on Windows.
        // Opus stays out — it would need the bassopus plugin, which isn't shipped.
        AssetType.RegisterType(new AssetType("Audio", [
                                       FileExtensionRegistry.GetUniqueId("wav"),
                                       FileExtensionRegistry.GetUniqueId("mp3"),
                                       FileExtensionRegistry.GetUniqueId("ogg"),
                                       FileExtensionRegistry.GetUniqueId("flac"),
                                       FileExtensionRegistry.GetUniqueId("aiff"),
                                       FileExtensionRegistry.GetUniqueId("aif"),
                                       FileExtensionRegistry.GetUniqueId("m4a"),
                                       FileExtensionRegistry.GetUniqueId("aac"),
                                       FileExtensionRegistry.GetUniqueId("wma"),
                                   ])
                                   {
                                       PrimaryOperators = [new Guid("65e95f77-4743-437f-ab31-f34b831d28d7")], // PlayAudioSample (graph)
                                       TimelineClipOperator = new Guid("f0008b50-091d-4e9f-91eb-baa212acfa20"), // AudioClip (timeline)
                                       RecordingFolder = "audio",
                                       Color = UiColors.ColorForValues,
                                       IconId = (uint)Icon.FileAudio,
                                       Subfolders = ["audio", "soundtrack","samples"],

                                   });
        // Live-session recording: .data files hold a serialised DataSet (see
        // Core/DataTypes/DataSet/DataSetCache.cs). Drop on the graph creates an
        // LoadDataClip op wired to the file; drop on the timeline clip area is a future
        // generalisation.
        AssetType.RegisterType(new AssetType("Data", [
                                       FileExtensionRegistry.GetUniqueId("data"),
                                   ])
                                   {
                                       PrimaryOperators =
                                               [new Guid("4d1c0e80-7b2a-4f6d-9c1b-12d3e4f50607")], // LoadDataClip
                                       TimelineClipOperator = new Guid("4d1c0e80-7b2a-4f6d-9c1b-12d3e4f50607"), // LoadDataClip
                                       RecordingFolder = "dataclips",
                                       Color = UiColors.ColorForCommands,
                                       IconId = (uint)Icon.FileDocument,
                                       Subfolders = ["dataclips", "data"],
                                   });
        // MIDI files convert to DataClips with the recording channel conventions (see
        // IoServices/MidiFileToDataSet.cs), so they replay through SimulateIoData like
        // recorded .data clips.
        AssetType.RegisterType(new AssetType("Midi", [
                                       FileExtensionRegistry.GetUniqueId("mid"),
                                       FileExtensionRegistry.GetUniqueId("midi"),
                                   ])
                                   {
                                       PrimaryOperators =
                                               [new Guid("b4766419-8bca-4fa0-a398-e6af90ef8971")], // MidiClip
                                       TimelineClipOperator = new Guid("b4766419-8bca-4fa0-a398-e6af90ef8971"), // MidiClip
                                       Color = UiColors.ColorForCommands,
                                       IconId = (uint)Icon.FileAudio,
                                       Subfolders = ["midi", "music"],
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