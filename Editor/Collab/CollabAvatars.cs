#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using T3.Core.Logging;
using T3.Core.Resource;
using T3.Graphics.Compat;

namespace T3.Editor.Collab;

/// <summary>
/// Downloads Discord avatars once and keeps their shader resource views for ImGui. Files are
/// cached on disk per URL hash so a restart doesn't refetch them. Failing URLs are remembered
/// and not retried this run.
/// </summary>
internal static class CollabAvatars
{
    private static readonly Dictionary<string, ShaderResourceView> _textures = new();
    private static readonly HashSet<string> _failed = new();
    private static readonly HashSet<string> _loading = new();
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly string _cacheFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                                               "TiXL", "avatars");

    /// <summary>The downloaded avatar's view for ImGui, or null while it loads (or when there is none).</summary>
    public static ShaderResourceView? GetTexture(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        // The URL arrives from other participants over the wire: only the legit Discord-CDN
        // prefix may be fetched, anything else (or any non-https URL) is ignored.
        if (!url.StartsWith("https://cdn.discordapp.com/", StringComparison.Ordinal))
            return null;

        if (_textures.TryGetValue(url, out var texture))
            return texture;

        if (_failed.Contains(url) || !_loading.Add(url))
            return null;

        _ = Task.Run(async () =>
                         {
                             try
                             {
                                 var cachePath = CachePath(url);
                                 if (!File.Exists(cachePath))
                                 {
                                     var bytes = await _http.GetByteArrayAsync(url);
                                     Directory.CreateDirectory(_cacheFolder);
                                     File.WriteAllBytes(cachePath, bytes);
                                 }

                                 var compat = await T3.Editor.Gui.UiHelpers.Thumbnails.ThumbnailManager.LoadImageTexture(cachePath);
                                 if (compat == null)
                                 {
                                     _failed.Add(url);
                                     return;
                                 }

                                 var srv = SrvManager.GetSrvForTexture(compat);
                                 if (srv != null)
                                     _textures[url] = srv;
                                 else
                                     _failed.Add(url);
                             }
                             catch (Exception e)
                             {
                                 Log.Debug($"Collab avatars: could not load '{url}': {e.Message}");
                                 _failed.Add(url);
                             }
                             finally
                             {
                                 _loading.Remove(url);
                             }
                         });
        return null;
    }

    private static string CachePath(string url)
    {
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(url)));
        return Path.Combine(_cacheFolder, hash[..24] + ".png");
    }
}
