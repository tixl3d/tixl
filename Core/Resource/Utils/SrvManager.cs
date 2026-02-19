using System;
using System.Collections.Generic;
using SharpDX.Direct3D11;
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
            
        _srvForTextures[texture]= srv;
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
        var keysForRemoval = new List<KeyValuePair<Texture2D, ShaderResourceView>>();
        foreach (var pair in _srvForTextures)
        {
            if (pair.Key.IsDisposed)
            {
                keysForRemoval.Add(pair);
            }
        }

        foreach (var (texture,srv) in keysForRemoval)
        {
            //Log.Debug("Disposing SRV...");
            _srvForTextures.Remove(texture);
            srv.Dispose();
        }
    }

    private static readonly Dictionary<Texture2D, ShaderResourceView> _srvForTextures = new();
}