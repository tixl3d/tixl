#nullable enable
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Xml.Linq;
using T3.Core.Compilation;

namespace T3.Editor.ExtensionPackages;

/// <summary>
/// The extensions published for this TiXL version: the extensions.json index attached to its GitHub release, which
/// CI writes with Installer/pack-extensions.py. Downloads and staging run on background tasks; the UI reads the
/// immutable <see cref="State"/> and <see cref="Operation"/> snapshots.
/// </summary>
internal static class ExtensionCatalog
{
    internal enum LoadStatus
    {
        NotLoaded,
        Loading,
        Loaded,
        Failed,
    }

    internal sealed record Extension(
        string Id,
        string Name,
        string Title,
        string Description,
        string Licence,
        string[] Platforms,
        long Size,
        string Sha256,
        string Version,
        Uri PackageUri)
    {
        public bool IsAvailableHere => Platforms.Contains(InstalledExtensions.CurrentPlatform);
    }

    internal sealed record CatalogState(LoadStatus Status, IReadOnlyList<Extension> Extensions, string? Error);

    internal sealed record OperationState(bool IsRunning, string? Message, bool Failed);

    public static CatalogState State { get; private set; } = new(LoadStatus.NotLoaded, [], null);
    public static OperationState Operation { get; private set; } = new(false, null, false);

    /** Set to a URL or a local folder/file to read extensions from somewhere else than the release (testing, offline). */
    public const string IndexOverrideVariable = "TIXL_EXTENSIONS_INDEX";

    public static string IndexLocation =>
        Environment.GetEnvironmentVariable(IndexOverrideVariable) is { Length: > 0 } location
            ? location
            : $"https://github.com/tixl3d/tixl/releases/download/v{Program.Version}/extensions.json";

    public static void LoadIfNeeded()
    {
        if (State.Status == LoadStatus.NotLoaded)
            Reload();
    }

    public static void Reload()
    {
        if (State.Status == LoadStatus.Loading)
            return;

        State = new CatalogState(LoadStatus.Loading, State.Extensions, null);
        Task.Run(LoadIndexAsync);
    }

    /// <summary>
    /// Stages installs for the wanted extensions that are missing or outdated and removals for installed ones no
    /// longer wanted. Returns true when the changes are staged and the editor should restart.
    /// </summary>
    public static Task<bool> StageChangesAsync(IReadOnlyCollection<string> wantedNames)
    {
        var extensions = State.Extensions;
        return RunOperationAsync(async () =>
                                 {
                                     var installed = InstalledExtensions.ReadInstalled();
                                     var pending = InstalledExtensions.ReadPending();
                                     var stagedNow = new Dictionary<string, InstalledExtensions.InstalledExtension>();

                                     foreach (var extension in extensions)
                                     {
                                         if (!wantedNames.Contains(extension.Name) || !extension.IsAvailableHere)
                                             continue;

                                         var isCurrent = installed.TryGetValue(extension.Name, out var existing) && existing.Sha256 == extension.Sha256;
                                         var isStaged = pending.Install.TryGetValue(extension.Name, out var staged) && staged.Sha256 == extension.Sha256;
                                         if (isCurrent || isStaged)
                                             continue;

                                         Operation = new OperationState(true, $"Downloading {extension.Title}...", false);
                                         var archive = await DownloadVerifiedAsync(extension);
                                         try
                                         {
                                             Operation = new OperationState(true, $"Unpacking {extension.Title}...", false);
                                             StageArchive(archive, extension.Name);
                                         }
                                         finally
                                         {
                                             File.Delete(archive);
                                         }

                                         stagedNow[extension.Name] = new InstalledExtensions.InstalledExtension(extension.Id, extension.Title, extension.Version,
                                                                                                                 extension.Sha256);
                                     }

                                     foreach (var (name, extension) in stagedNow)
                                         pending.Install[name] = extension;

                                     foreach (var name in installed.Keys)
                                     {
                                         if (!wantedNames.Contains(name) && !pending.Remove.Contains(name))
                                             pending.Remove.Add(name);
                                     }

                                     // Unticking an extension that is only staged cancels its installation
                                     foreach (var name in pending.Install.Keys.ToList())
                                     {
                                         if (wantedNames.Contains(name))
                                             continue;

                                         pending.Install.Remove(name);
                                         var staging = InstalledExtensions.StagingDirectory(name);
                                         if (Directory.Exists(staging))
                                             Directory.Delete(staging, recursive: true);
                                     }

                                     pending.Remove.RemoveAll(wantedNames.Contains);
                                     InstalledExtensions.WritePending(pending);
                                 });
    }

    /** Stages an extension from a .nupkg file, e.g. downloaded by hand or built locally with pack-extensions.py. */
    public static Task<bool> StageFromFileAsync(string path)
    {
        return RunOperationAsync(() =>
                                 {
                                     Operation = new OperationState(true, $"Unpacking {Path.GetFileName(path)}...", false);
                                     var (id, title, version, platforms) = ReadPackageIdentity(path);
                                     if (platforms.Length > 0 && !platforms.Contains(InstalledExtensions.CurrentPlatform))
                                         throw new InvalidDataException($"{title} is built for {string.Join(", ", platforms)}, not for {InstalledExtensions.CurrentPlatform}.");

                                     var name = id.StartsWith(OfficialIdPrefix, StringComparison.Ordinal) ? id[OfficialIdPrefix.Length..] : id;
                                     StageArchive(path, name);

                                     using var stream = File.OpenRead(path);
                                     var sha256 = Convert.ToHexStringLower(SHA256.HashData(stream));
                                     var pending = InstalledExtensions.ReadPending();
                                     pending.Install[name] = new InstalledExtensions.InstalledExtension(id, title, version, sha256);
                                     pending.Remove.Remove(name);
                                     InstalledExtensions.WritePending(pending);
                                     return Task.CompletedTask;
                                 });
    }

    private static async Task<bool> RunOperationAsync(Func<Task> operation)
    {
        if (Operation.IsRunning)
            return false;

        Operation = new OperationState(true, null, false);
        try
        {
            await Task.Run(operation);
            Operation = new OperationState(false, null, false);
            return true;
        }
        catch (Exception e)
        {
            Log.Warning($"Extensions: {e.Message}");
            Operation = new OperationState(false, e.Message, true);
            return false;
        }
    }

    private static async Task LoadIndexAsync()
    {
        try
        {
            var indexUri = ToUri(IndexLocation);
            var json = indexUri.IsFile
                           ? await File.ReadAllTextAsync(indexUri.LocalPath)
                           : await _http.GetStringAsync(indexUri);
            var index = JsonSerializer.Deserialize<IndexFile>(json, _jsonOptions)
                        ?? throw new InvalidDataException("The extension index is empty.");

            var extensions = new List<Extension>(index.Extensions.Count);
            foreach (var entry in index.Extensions)
            {
                extensions.Add(new Extension(entry.Id, entry.Name, entry.Title, entry.Description, entry.Licence, entry.Platforms, entry.Size,
                                             entry.Sha256.ToLowerInvariant(), index.TixlVersion, new Uri(indexUri, entry.File)));
            }

            extensions.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));
            State = new CatalogState(LoadStatus.Loaded, extensions, null);
        }
        catch (HttpRequestException e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            State = new CatalogState(LoadStatus.Failed, [], $"No extensions are published for TiXL {Program.Version}.");
        }
        catch (Exception e)
        {
            State = new CatalogState(LoadStatus.Failed, [], $"Could not load the list of extensions: {e.Message}");
        }
    }

    /** Downloads a package to a temporary file and checks it against the index. Returns the file path. */
    private static async Task<string> DownloadVerifiedAsync(Extension extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"tixl-{extension.Name}-{Guid.NewGuid():N}.nupkg");
        try
        {
            if (extension.PackageUri.IsFile)
            {
                File.Copy(extension.PackageUri.LocalPath, path);
            }
            else
            {
                using var response = await _http.GetAsync(extension.PackageUri, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                await using var target = File.Create(path);
                await response.Content.CopyToAsync(target);
            }

            await using var stream = File.OpenRead(path);
            var sha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream));
            if (stream.Length != extension.Size || sha256 != extension.Sha256)
                throw new InvalidDataException($"The download of {extension.Title} is damaged (size or checksum differs). Please try again.");

            return path;
        }
        catch
        {
            File.Delete(path);
            throw;
        }
    }

    /// <summary>
    /// Unpacks the package folder of a .nupkg into the staging folder of the extension and checks that it is a TiXL
    /// operator package for this version.
    /// </summary>
    private static void StageArchive(string archivePath, string name)
    {
        var staging = InstalledExtensions.StagingDirectory(name);
        if (Directory.Exists(staging))
            Directory.Delete(staging, recursive: true);
        Directory.CreateDirectory(staging);

        var stagingRoot = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;
        using (var archive = ZipFile.OpenRead(archivePath))
        {
            foreach (var entry in archive.Entries)
            {
                if (!entry.FullName.StartsWith(PackageFolderInArchive, StringComparison.Ordinal) || entry.FullName.EndsWith('/'))
                    continue;

                var target = Path.GetFullPath(Path.Combine(staging, entry.FullName[PackageFolderInArchive.Length..]));
                if (!target.StartsWith(stagingRoot, StringComparison.Ordinal))
                    throw new InvalidDataException($"{Path.GetFileName(archivePath)} contains a file outside its package folder.");

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
            }
        }

        if (!AssemblyInformation.TryLoadReleaseInfo(staging, out var releaseInfo))
        {
            Directory.Delete(staging, recursive: true);
            throw new InvalidDataException($"{Path.GetFileName(archivePath)} is not a TiXL extension.");
        }

        var editorVersion = Program.Version;
        if (releaseInfo.EditorVersion.Major != editorVersion.Major || releaseInfo.EditorVersion.Minor != editorVersion.Minor)
        {
            Directory.Delete(staging, recursive: true);
            throw new InvalidDataException($"{Path.GetFileName(archivePath)} was built for TiXL {releaseInfo.EditorVersion}, this is {editorVersion}.");
        }
    }

    /** Platforms come from the nuspec tags that pack-extensions.py writes ("tixl win-x64 linux-x64"). */
    private static (string Id, string Title, string Version, string[] Platforms) ReadPackageIdentity(string archivePath)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var nuspecEntry = archive.Entries.FirstOrDefault(e => !e.FullName.Contains('/') && e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
                          ?? throw new InvalidDataException($"{Path.GetFileName(archivePath)} is not a NuGet package.");

        using var stream = nuspecEntry.Open();
        var metadata = XDocument.Load(stream).Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "metadata")
                       ?? throw new InvalidDataException($"{Path.GetFileName(archivePath)} has no package metadata.");

        string? Value(string name) => metadata.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value;
        var isTixlPackage = metadata.Descendants().Any(e => e.Name.LocalName == "packageType" && (string?)e.Attribute("name") == PackageType);
        if (!isTixlPackage)
            throw new InvalidDataException($"{Path.GetFileName(archivePath)} is not a TiXL extension.");

        var id = Value("id") ?? throw new InvalidDataException("The package has no id.");
        var platforms = (Value("tags") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                             .Where(tag => tag.Contains('-'))
                                             .ToArray();
        return (id, Value("title") ?? id, Value("version") ?? "", platforms);
    }

    private static Uri ToUri(string location)
    {
        if (Uri.TryCreate(location, UriKind.Absolute, out var uri) && !uri.IsFile)
            return uri;

        var path = Path.GetFullPath(location);
        if (Directory.Exists(path))
            path = Path.Combine(path, "extensions.json");
        return new Uri(path);
    }

    private sealed record IndexEntry(
        string Id,
        string Name,
        string Title,
        string Description,
        string Licence,
        string[] Platforms,
        string File,
        long Size,
        string Sha256);

    private sealed record IndexFile(int FormatVersion, string TixlVersion, List<IndexEntry> Extensions);

    /** Written by Installer/pack-extensions.py; the package folder sits under this prefix next to the NuGet metadata. */
    private const string PackageFolderInArchive = "tixl/";

    private const string PackageType = "TixlOperatorPackage";
    private const string OfficialIdPrefix = "TiXL.";

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(10) };

    private static readonly JsonSerializerOptions _jsonOptions = new()
                                                                     {
                                                                         PropertyNameCaseInsensitive = true,
                                                                         NumberHandling = JsonNumberHandling.AllowReadingFromString,
                                                                     };
}
