using SharpDX.WIC;
using T3.Core.Utils;

namespace Lib.image.generate.load;

[Guid("61ec6355-bd7d-4abb-aa44-b01b7d658e23")]
internal sealed class LoadImageFromUrl : Instance<LoadImageFromUrl>
{
    [Output(Guid = "316fe874-5178-4068-a233-6c6ecf70c49e")]
    public readonly Slot<Texture2D> Texture = new();

    [Output(Guid = "a843ab64-0b99-4c4f-9644-cc9bb771981d")]
    public readonly Slot<ShaderResourceView> ShaderResourceView = new();

    public LoadImageFromUrl()
    {
        Texture.UpdateAction += UpdateShaderResourceView;
        ShaderResourceView.UpdateAction += UpdateShaderResourceView;
    }

    private bool _triggerUpdate;
        
    private void UpdateShaderResourceView(EvaluationContext context)
    {
        var wasUpdated = MathUtils.WasTriggered(TriggerUpdate.GetValue(context), ref _triggerUpdate);
            
        var url = Url.GetValue(context);
        if (wasUpdated || url != _url && !string.IsNullOrEmpty(url))
        {
            if (wasUpdated && !TriggerUpdate.HasInputConnections)
            {
                TriggerUpdate.SetTypedInputValue(false);
            }
                    
                    
            _url = url;
            HttpClient httpClient = null;
            //Dispose();

            if (_httpClient == null)
            {
                try
                {
                    httpClient = new HttpClient(new HttpClientHandler() { UseDefaultCredentials = true });
                    _httpClient = httpClient;
                }
                catch (Exception e)
                {
                    Log.Error($"failed to create http client: {e.Message}", SymbolChildId);
                    return; // just keep the old image, if we have one
                }
            }
            try { 
                DownloadImage(_httpClient, url);
            }
            catch (Exception e)
            {
                Log.Error($"failed to parse image: {e.Message}", SymbolChildId);
            }
        }

        Texture.Value = _image;
    }

    private async void DownloadImage(HttpClient client, String url)
    {
        if (client == null) throw new ArgumentNullException("httpClient");

        Stream stream = await DoGetRequest(client, url);
        if (stream != null)
        {
            HandleResponse(stream);
            stream.Close();
        }
    }
    private async Task<Stream> DoGetRequest(HttpClient client, String url)
    {
        try
        {
            var response = await client.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var streamResponse = await response.Content.ReadAsStreamAsync();
                return streamResponse;
            }
            // 404 etc.
            Log.Info($"No success loading {url}: {response.StatusCode}", this);
            return null;
        } catch (Exception e)
        {
            Log.Info($"Failed to load URL : {e.Message}", this);
            return null;
        }
    }


    void HandleResponse(Stream streamResponse)
    {
        lock (this)
        {
            _image = null;
            using var memStream = new MemoryStream();
            try
            {
                if (streamResponse != null)
                {
                    streamResponse.CopyTo(memStream);

                    Log.Debug($"Finished loading URL {_url}", this);

                    ImagingFactory factory = new ImagingFactory();
                    memStream.Position = 0;
                    var bitmapDecoder = new BitmapDecoder(factory, memStream, DecodeOptions.CacheOnDemand);
                    var formatConverter = new FormatConverter(factory);
                    var bitmapFrameDecode = bitmapDecoder.GetFrame(0);
                    formatConverter.Initialize(bitmapFrameDecode, PixelFormat.Format32bppRGBA, BitmapDitherType.None, null, 0.0,
                                               BitmapPaletteType.Custom);

                    _image?.Dispose();
                    _image = Texture2D.CreateFromBitmap(ResourceManager.Device, formatConverter);
                    _image.Name = _url;
                    bitmapFrameDecode.Dispose();
                    bitmapDecoder.Dispose();
                    formatConverter.Dispose();
                    factory.Dispose();
                    Texture.DirtyFlag.Invalidate();
                }
            }
            catch (Exception e)
            {
                Log.Info($"Failed to decode image data: {e.Message}", this);
            }
        }
    }

    private HttpClient _httpClient;
    private Texture2D _image;
    private string _url;

    [Input(Guid = "21b2e219-0b2a-4323-b288-f39ed791e676")]
    public readonly InputSlot<string> Url = new();
        
    [Input(Guid = "710BA03D-D26E-496A-B6CD-68ABB2E9F369")]
    public readonly InputSlot<bool> TriggerUpdate = new();        
}