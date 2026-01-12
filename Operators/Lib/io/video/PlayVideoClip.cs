using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Mathematics.Interop;
using SharpDX.MediaFoundation;
using T3.Core.Audio;
using T3.Core.Utils;

namespace Lib.io.video;

[Guid("04c1a6dc-3042-48a8-81d2-0a5a162016dc")]
/**
 * This code is strongly inspired by
 *
 * https://github.com/vvvv/VL.Video.MediaFoundation/blob/master/src/VideoPlayer.cs
 */
internal sealed class PlayVideoClip : Instance<PlayVideoClip>
{
    [Output(Guid = "eb954aeb-535b-4b22-ac49-858f71bdaac4", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Texture2D> Texture = new();

    [Output(Guid = "30357595-0893-47F8-8BCA-22DD77275768", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly TimeClipSlot<Command> TimeSlot = new();

    // Input parameters
    [Input(Guid = "10c311ee-6426-463a-a1fe-cfac6de04224")]
    public readonly InputSlot<Command> Command = new();

    [Input(Guid = "31721e18-556b-452b-a8aa-18dbd44af74d")]
    public readonly InputSlot<string> Path = new();

    [Input(Guid = "28f27625-37fe-409a-b6c1-d4eabf6c1eb8")]
    public readonly InputSlot<float> Volume = new();

    [Input(Guid = "5EB10090-AE6A-4AE7-9FBD-5BD9FFD13B1B")]
    public readonly InputSlot<float> ResyncThreshold = new();

    public PlayVideoClip()
    {
        Texture.UpdateAction += Update;
        TimeSlot.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        Command.GetValue(context);

        // Initialize media foundation library and default values
        if (!_initialized)
        {
            SetupMediaFoundation();
            _initialized = true;
        }

        // Change texture size if necessary
        if (Texture.DirtyFlag.IsDirty
            || _size.Width <= 0 || _size.Height <= 0)
        {
            SetupTexture(_size);
        }

        if (_engine == null)
            return;

        // Update video if url has changed
        var reloadedPath = false;
        if (Path.DirtyFlag.IsDirty)
        {
            var filePath = Path.GetValue(context);
            if (!TryGetFilePath(filePath, out var absolutePath))
            {
                Log.Error($"Could not find file: {filePath}", this);
            }
            else
            {
                reloadedPath = true;
                MediaUrl = absolutePath;
                _engine.Pause();
            }
        }

        // get scaled time range of video
        var timeRange = TimeSlot.TimeClip.TimeRange;
        var sourceRange = TimeSlot.TimeClip.SourceRange;

        // get the time we should be at in the video according to the timeline
        var barsInSeconds = context.LocalTime - timeRange.Start;
        if (timeRange.End != timeRange.Start)
        {
            var rate = (sourceRange.End - sourceRange.Start)
                       / (timeRange.End - timeRange.Start);
            barsInSeconds *= rate;
            PlaybackRate = (float)rate;
        }
        barsInSeconds += sourceRange.Start;
        var shouldBeTimeInSecs = context.Playback.SecondsFromBars(barsInSeconds);

        // find start and end of video that should be played back
        var sourceStart = context.Playback.SecondsFromBars(sourceRange.Start);
        var sourceEnd = context.Playback.SecondsFromBars(sourceRange.End);
        var videoStart = Math.Min(sourceStart, sourceEnd);
        var videoEnd = Math.Max(sourceStart, sourceEnd);
        videoStart = Math.Clamp(videoStart, 0.0, _engine.Duration);
        videoEnd = Math.Clamp(videoEnd, 0.0, _engine.Duration);

        // shall we seek?
        var isExporting = context.Playback.IsRenderingToFile;
        var clampedTime = Math.Clamp(shouldBeTimeInSecs, videoStart, videoEnd);
        var videoTime = Math.Clamp(_engine.CurrentTime, videoStart, videoEnd);
        var deltaTime = clampedTime - videoTime;
        var threshold = isExporting ? 0.01f : ResyncThreshold.GetValue(context);
        var shouldSeek = reloadedPath || (!_engine.IsSeeking
                                          && Math.Abs(deltaTime) > threshold);

        // Play when we are in the center portion of the video
        // and we are playing the video forward
        _play = !isExporting && (reloadedPath ||
                (shouldBeTimeInSecs == clampedTime && clampedTime - _lastUpdateTime > 0.0));
        _lastUpdateTime = clampedTime;

        // initiate seeking if necessary
        if (shouldSeek)
        {
            Log.Debug($"Seeking video to {clampedTime:0.00} delta was {deltaTime:0.0000)}s", this);
            SeekTime = (float)clampedTime;
            Seek = true;
        }

        /***
         * Mute video if audio engine is muted
         * FIXME: does not work when the video is not updating...
         *
         * Fixing this will require some thought: To managed audio-levels and playback centrally we probably need
         * an interfaces to register all audio sources and provides functions like muting, stop, setting audio level, etc.
         */
        if (AudioEngine.IsMuted)
        {
            _engine.Volume = 0.0;
        }
        else
        {
            _engine.Volume = Volume.GetValue(context).Clamp(0f, 1f);
        }

        UpdateVideo();
    }

    private void SetupMediaFoundation()
    {
        using var mediaEngineAttributes = new MediaEngineAttributes
                                              {
                                                  // _SRGB doesn't work :/ Getting invalid argument exception later in TransferVideoFrame
                                                  AudioCategory = SharpDX.Multimedia.AudioStreamCategory.GameMedia,
                                                  AudioEndpointRole = SharpDX.Multimedia.AudioEndpointRole.Multimedia,
                                                  VideoOutputFormat = (int)Format.B8G8R8A8_UNorm
                                              };

        var device = ResourceManager.Device;
        if (device != null)
        {
            // Add multi thread protection on device (MF is multi-threaded)
            using var deviceMultithread = device.QueryInterface<DeviceMultithread>();
            deviceMultithread.SetMultithreadProtected(true);

            // Reset device
            using var manager = new DXGIDeviceManager();
            manager.ResetDevice(device);
            mediaEngineAttributes.DxgiManager = manager;
        }

        // Setup Media Engine attributes and create a DXGI Device Manager
        _dxgiDeviceManager = new DXGIDeviceManager();
        _dxgiDeviceManager.ResetDevice(device);
        var attributes = new MediaEngineAttributes
                             {
                                 DxgiManager = _dxgiDeviceManager,
                                 VideoOutputFormat = (int)Format.B8G8R8A8_UNorm
                                 //VideoOutputFormat = (int)SharpDX.DXGI.Format.NV12                                     
                             };

        MediaManager.Startup();
        using var factory = new MediaEngineClassFactory();
        _engine = new MediaEngine(factory, attributes, MediaEngineCreateFlags.None, EnginePlaybackEventHandler);
    }

    private void SetupTexture(Int2 size)
    {
        if (size.Width <= 0 || size.Height <= 0)
            size = new Int2(512, 512);

        Texture.DirtyFlag.Clear();

        if (_texture != null && size == _size)
            return;

        try
        {
            _texture = Texture2D.CreateTexture2D(
                                                 new Texture2DDescription
                                                     {
                                                         ArraySize = 1,
                                                         BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource | BindFlags.UnorderedAccess,
                                                         CpuAccessFlags = CpuAccessFlags.None,
                                                         Format = Format.B8G8R8A8_UNorm,
                                                         Width = size.Width,
                                                         Height = size.Height,
                                                         MipLevels = 1,
                                                         OptionFlags = ResourceOptionFlags.None,
                                                         SampleDescription = new SampleDescription(1, 0),
                                                         Usage = ResourceUsage.Default
                                                     });
            _size = size;
        }
        catch (Exception e)
        {
            Log.Warning($"Failed to create video texture with size {size}:" + e.Message);
        }
    }

    private void EnginePlaybackEventHandler(MediaEngineEvent mediaEvent, long param1, int param2)
    {
        // Log.Debug(mediaEvent.ToString(), this);
        switch (mediaEvent)
        {
            case MediaEngineEvent.LoadStart:
                LastErrorCode = MediaEngineErr.Noerror;
                break;
            case MediaEngineEvent.Error:
                LastErrorCode = (MediaEngineErr)param1;
                break;
            case MediaEngineEvent.LoadedMetadata:
                _invalidated = true;
                _engine.Volume = 0.0;
                Log.Debug("pausing...", this);
                _engine.Pause();
                break;
            case MediaEngineEvent.FirstFrameReady:
            case MediaEngineEvent.TimeUpdate:
                LastErrorCode = MediaEngineErr.Noerror;
                break;
        }
    }
        
    private string MediaUrl
    {
        set
        {
            if (value != _url)
            {
                _url = value;
                try
                {
                    _engine.Pause();
                    _engine.Source = value;
                }
                catch (SharpDXException e)
                {
                    Log.Debug("unable to switch video source..." + e.Message);
                }
            }
        }
    }

    private MediaEngine _engine;
    private DXGIDeviceManager _dxgiDeviceManager;
    private Int2 _size = new(0, 0);

    private string _url;
    private Texture2D _texture;
    private bool _invalidated;

    /** Set to true to start playback, false to pause playback. */
    private bool _play;

    private float SeekTime { get; set; }
    private bool Seek { get; set; }
    private float LoopStartTime { get; set; }
    private float LoopEndTime { get; set; } = float.MaxValue;

    /** The normalized source rectangle. */
    private RectangleF? SourceBounds { get; set; }

    /** The border color. */
    private Color4? BorderColor { get; set; }

    /** The size of the output texture. Use zero to take the size from the video. */
    public Int2 TextureSize
    {
        set
        {
            if (value != _textureSize)
            {
                _textureSize = value;
                _invalidated = true;
            }
        }
    }

    private Int2 _textureSize;

    /** Gets the most recent error status. */
    private MediaEngineErr LastErrorCode { get; set; }

    private void UpdateVideo()
    {
        if (ReadyState <= ReadyStates.HaveNothing)
        {
            _texture = null; // FIXME: this is probably stupid
            return;
        }

        if (ReadyState >= ReadyStates.HaveMetadata)
        {
            if (Seek)
            {
                var seekTime = SeekTime.Clamp(0, Duration);
                _engine.CurrentTime = seekTime;
                Seek = false;
            }

            if (Loop)
            {
                var currentTime = CurrentTime;
                var loopStartTime = LoopStartTime.Clamp(0f, Duration);
                var loopEndTime = (LoopEndTime < 0 ? float.MaxValue : LoopEndTime).Clamp(0f, Duration);
                if (currentTime < loopStartTime || currentTime > loopEndTime)
                {
                    if (PlaybackRate >= 0)
                        _engine.CurrentTime = loopStartTime;
                    else
                        _engine.CurrentTime = loopEndTime;
                }
            }

            if (_play && _engine.IsPaused)
                _engine.Play();

            else if (!_play && !_engine.IsPaused)
                _engine.Pause();
        }

        if (ReadyState < ReadyStates.HaveCurrentData || !_engine.OnVideoStreamTick(out var presentationTimeTicks))
            return;

        if (_invalidated || _texture == null)
        {
            _invalidated = false;

            _engine.GetNativeVideoSize(out var width, out var height);
            Log.Debug($"should set size to: {width}x{height}", this);
            SetupTexture(new Int2(width, height));

            // _SRGB doesn't work :/ Getting invalid argument exception in TransferVideoFrame
            //_renderTarget = Texture.New2D(graphicsDevice, width, height, PixelFormat.B8G8R8A8_UNorm, TextureFlags.RenderTarget | TextureFlags.ShaderResource);
        }

        if (_texture == null)
            return;

        _engine.TransferVideoFrame(
                                   (SharpDX.Direct3D11.Texture2D)_texture,
                                   ToVideoRect(SourceBounds),
                                   //new RawRectangle(0, 0, renderTarget.ViewWidth, renderTarget.ViewHeight),
                                   new RawRectangle(0, 0, _textureSize.Width, _textureSize.Height),
                                   ToRawColorBgra(BorderColor));
        Texture.Value = _texture;
    }

    private static VideoNormalizedRect? ToVideoRect(RectangleF? rect)
    {
        if (rect.HasValue)
        {
            var r = rect.Value;
            return new VideoNormalizedRect()
                       {
                           Left = r.Left.Clamp(0f, 1f),
                           Bottom = r.Bottom.Clamp(0f, 1f),
                           Right = r.Right.Clamp(0f, 1f),
                           Top = r.Top.Clamp(0f, 1f)
                       };
        }

        return default;
    }

    private static RawColorBGRA? ToRawColorBgra(Color4? color)
    {
        if (color.HasValue)
        {
            color.Value.ToBgra(out var r, out var g, out var b, out var a);
            return new RawColorBGRA(b, g, r, a);
        }

        return default;
    }

    // FIXME: we should call this properly
    protected override void Dispose(bool disposing)
    {
        Log.Warning($"Disposing video player");
        base.Dispose(disposing);
        _engine.Shutdown();
        _engine.PlaybackEvent -= EnginePlaybackEventHandler;
        _engine.Dispose();
        _texture.Dispose();
        //colorSpaceConverter.Dispose();
        //renderTarget?.Dispose();
    }

    #region Forward engine properties
        
    private float PlaybackRate { get => (float)_engine.PlaybackRate; set => _engine.PlaybackRate = value; }
        
    private bool Loop { get => _engine.Loop; set => _engine.Loop = value; }
        
    /** Whether or not playback started. */
    public bool Playing => !_engine.IsPaused;

    /** A Boolean which is true if the media contained in the element has finished playing. */
    public bool IsEnded => _engine.IsEnded;

    /** The current playback time in seconds */
    private float CurrentTime => (float)_engine.CurrentTime;

    /** The length of the element's media in seconds. */
    private float Duration => (float)_engine.Duration;

    /** The readiness state of the media. */
    private ReadyStates ReadyState => (ReadyStates)_engine.ReadyState;
    #endregion

    private enum ReadyStates : short
    {
        /** information is available about the media resource. */
        HaveNothing,

        /** ugh of the media resource has been retrieved that the metadata attributes are initialized. Seeking will no longer raise an exception. */
        HaveMetadata,

        /** a is available for the current playback position, but not enough to actually play more than one frame. */
        HaveCurrentData,

        /** a for the current playback position as well as for at least a little bit of time into the future is available (in other words, at least two frames of video, for example). */
        HaveFutureData,

        /** ugh data is available—and the download rate is high enough—that the media can be played through to the end without interruption.*/
        HaveEnoughData
    }

    private bool _initialized;
    private double _lastUpdateTime;
}