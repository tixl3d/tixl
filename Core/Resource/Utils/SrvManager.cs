using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using T3.Graphics.Compat;
using T3.Core.Logging;

namespace T3.Core.Resource;

/// <summary>
/// A helper class that manages the creating of srv's for textures
/// </summary>
public static class SrvManager
{
    public static ShaderResourceView GetSrvForTexture(Texture2D texture)
    {
        if (_srvForTextures.TryGetValue(texture, out var srv)
            && !srv.IsDisposed)
        {
            return srv;
        }
            
        srv = CreateNewSrv(texture);
        if (srv == null)
            return null;
            
        _srvForTextures.AddOrUpdate(texture, srv);
        return srv;
    }

    private static ShaderResourceView CreateNewSrv(Texture2D texture)
    {
        try
        {
            //Log.Debug($"Create srv for {texture.Description.Width}×{texture.Description.Height}");
            return (texture.Description.BindFlags & BindFlags.DepthStencil) > 0 
                       ? null : // skip here for depth/stencil to prevent warning below
                       new ShaderResourceView(ResourceManager.Device, texture);
        }
        catch (Exception e)
        {
            Log.Warning("SrvManager::CreateNewSrv(...) - Could not create ShaderResourceView for texture. " + e.Message);
            return null;
        }
    }

        
    public static void RemoveForDisposedTextures()
    {
        _keysForRemoval.Clear();
        foreach (var pair in (IEnumerable<KeyValuePair<Texture2D, ShaderResourceView>>)_srvForTextures)
        {
            if (pair.Key.IsDisposed)
            {
                _keysForRemoval.Add(pair);
            }
        }

        foreach (var (texture,srv) in _keysForRemoval)
        {
            //Log.Debug("Disposing SRV...");
            _srvForTextures.Remove(texture);
            srv.Dispose();
        }

        _keysForRemoval.Clear();
    }

    /// <summary>
    /// Weak by texture: a texture an operator dropped without disposing must not be kept alive by its view,
    /// or neither can ever be collected.
    /// </summary>
    private static readonly ConditionalWeakTable<Texture2D, ShaderResourceView> _srvForTextures = new();

    private static readonly List<KeyValuePair<Texture2D, ShaderResourceView>> _keysForRemoval = new();
}