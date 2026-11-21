using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using T3.Core.DataTypes;
using T3.Core.Logging;
using T3.Core.Operator.Interfaces;
using T3.Core.Resource;
using T3.Core.Resource.Assets;

using T3.Core.Utils;
using T3.Graphics;
using T3.Graphics.Compat;

#pragma warning disable CS8981

namespace Lib.io.video.depthanything;

[Guid("4af2de92-6078-483c-b755-0b387cbfcb9a")]
public sealed class DepthScanBuffer : Instance<DepthScanBuffer>, IStatusProvider, ICustomDropdownHolder
{
    #region Outputs

    [Output(Guid = "a2592953-52fe-41e1-b8b2-c63ee025f5b8", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Texture2D?> HeldDepth = new();

    [Output(Guid = "58d2de23-cfc6-49a6-866d-4e30da6b50fb", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Texture2D?> MaskTexture = new();

    [Output(Guid = "76d74e26-37a3-42d9-8524-14538e6dba1f", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> Coverage = new();

    [Output(Guid = "911c67c5-1919-46fc-9a12-9d72452154bf", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> Captures = new();

    [Output(Guid = "ff5bb829-f235-4fe1-b3be-d5a256c2158a")]
    public readonly Slot<string> SavedPath = new();

    [Output(Guid = "2a7b3c4d-5e6f-4a7b-8c9d-0e1f2a3b4c5d", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Texture2D?> HeldColor = new();

    [Output(Guid = "3b8c4d5e-6f70-4b8c-9d0e-1f2a3b4c5d6e", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<BufferWithViews?> PointCloud = new();

    [Output(Guid = "4c9d5e6f-7081-4c9d-ae1f-2a3b4c5d6e7f", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> PointCount = new();

    [Output(Guid = "5d0e6f70-8192-4d0e-bf2a-3b4c5d6e7f8a", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<bool> Recording = new();

    #endregion

    public DepthScanBuffer()
    {
        HeldDepth.UpdateAction = Update;
        MaskTexture.UpdateAction = Update;
        Coverage.UpdateAction = Update;
        Captures.UpdateAction = Update;
        SavedPath.UpdateAction = Update;
        HeldColor.UpdateAction = Update;
        PointCloud.UpdateAction = Update;
        PointCount.UpdateAction = Update;
        Recording.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        HandleReset(context);
        HandleRecordTriggers(context);
        HandleSave(context);
        Recording.Value = _recording;

        var depthTexture = DepthMap.GetValue(context);
        if (depthTexture == null || depthTexture.IsDisposed)
        {
            // Dropping the input must not drop the held scan; stop the in-flight copy instead.
            _depthPending = false;
            _confPending = false;
            if (_captures == 0)
                SetStatus("Connect a depth map (e.g. DepthAnything's DepthTexture) and enable Capture.", IStatusProvider.StatusLevel.Notice);
            return;
        }

        var desc = depthTexture.Description;
        if (desc.Format != Format.R32_Float)
        {
            if (!_loggedWrongFormat)
            {
                _loggedWrongFormat = true;
                Log.Warning("[DepthScanBuffer] Unsupported depth format; expected R32_Float.", this);
            }

            SetStatus("Unsupported depth format; connect a R32_Float depth map (DepthAnything's DepthTexture).", IStatusProvider.StatusLevel.Warning);
            return;
        }

        if (_width != desc.Width || _height != desc.Height)
        {
            // A resized input starts a fresh scan: accumulating across sizes would corrupt every pixel.
            AllocateBuffers(desc.Width, desc.Height);
            SetStatus($"Depth size changed to {desc.Width}x{desc.Height}; the held scan was reset.", IStatusProvider.StatusLevel.Notice);
        }

        var capture = Capture.GetValue(context);
        var captureOnce = MathUtils.WasTriggered(CaptureOnce.GetValue(context), ref _captureOnceState);
        if (captureOnce)
        {
            CaptureOnce.SetTypedInputValue(false);
            _captureOnceState = false;
        }

        _uiPointGrid = Math.Clamp(PointGrid.GetValue(context), 0, 1);
        _uiStability = Math.Clamp(PointStability.GetValue(context), 0f, 1f);
        _uiMode = Mode.GetValue(context);
        _uiSaveFormat = SaveFormatInput.GetValue(context);
        _uiMaxCaptures = MaxCaptures.GetValue(context);

        var maxCaptures = Math.Max(0, MaxCaptures.GetValue(context));
        if (maxCaptures > 0 && _captures >= maxCaptures)
        {
            _recording = false;
            Recording.Value = false;
            SetStatus("Scan complete; save it with the Save input.", IStatusProvider.StatusLevel.Success);
            return;
        }

        var lifetime = Math.Max(0f, SampleLifetime.GetValue(context));
        if (lifetime > 0f && _captures > 0 && ExpireOldSamples(lifetime))
            return;

        // Complete the previous capture's readback and merge it before starting a new one.
        // A staging map is mapped, merged and unmapped within this one Update call, never across frames.
        if (_depthPending)
        {
            if (!TryMapStaging(ref _depthPending, _depthStaging, ref _waitFrames))
                return; // copy still in flight; the held scan is untouched this frame

            var confMapped = true;
            if (_confPending && !TryMapStaging(ref _confPending, _confStaging, ref _confWaitFrames))
                confMapped = false;

            var colorMapped = true;
            if (_colorPending && !TryMapStaging(ref _colorPending, _colorStaging, ref _colorWaitFrames))
                colorMapped = false;

            MergeAndUpload(confMapped, colorMapped);
            _confPending = false;
            _colorPending = false;
            _mappedDepth = null;
            _mappedConf = null;
            _mappedColor = null;

            // Capture ended while the copy was in flight: the merge above was valid, now hold.
            if (!capture && !captureOnce && !_recording)
            {
                UpdateHoldingStatus();
                return;
            }
        }

        if (!capture && !captureOnce && !_recording)
        {
            UpdateHoldingStatus();
            return;
        }

        // Issue the next readback copies: depth, plus confidence when connected and compatible.
        var confidence = Confidence.GetValue(context);
        var useConfidence = confidence is { IsDisposed: false }
                            && confidence.Description.Format == Format.R32_Float
                            && confidence.Description.Width == desc.Width
                            && confidence.Description.Height == desc.Height;
        if (confidence is { IsDisposed: false } && !useConfidence && !_loggedConfMismatch)
        {
            _loggedConfMismatch = true;
            Log.Warning("[DepthScanBuffer] Confidence texture ignored; size or format differs from the depth map.", this);
        }

        var colorTexture = ColorMap.GetValue(context);
        var useColor = colorTexture is { IsDisposed: false }
                       && colorTexture.Description.Format == Format.R8G8B8A8_UNorm
                       && colorTexture.Description.Width == desc.Width
                       && colorTexture.Description.Height == desc.Height;
        if (colorTexture is { IsDisposed: false } && !useColor && !_loggedColorMismatch)
        {
            _loggedColorMismatch = true;
            Log.Warning("[DepthScanBuffer] ColorMap ignored; size or format differs from the depth map.", this);
        }

        GetOrCreateStaging(desc.Width, desc.Height);
        var deviceContext = ResourceManager.Device.ImmediateContext;
        deviceContext.CopyResource(depthTexture, _depthStaging);
        _depthPending = true;
        _confPending = false;

        if (useConfidence)
        {
            deviceContext.CopyResource(confidence, _confStaging);
            _confPending = true;
        }

        if (useColor)
        {
            deviceContext.CopyResource(colorTexture, _colorStaging);
            _colorPending = true;
        }

        _waitFrames = 0;
        _confWaitFrames = 0;
        _colorWaitFrames = 0;
        _pendingThreshold = Math.Clamp(ConfidenceThreshold.GetValue(context), 0f, 1f);
        _pendingMode = (ScanMode)Math.Clamp(Mode.GetValue(context), 0, 3);
        _pendingFov = Math.Clamp(FieldOfView.GetValue(context), 1f, 179f);
        _pendingScale = Math.Max(0f, PointScale.GetValue(context));
        _pendingStride = Math.Clamp(PixelStride.GetValue(context), 1, 64);
        _pendingMaxPoints = Math.Max(256, MaxPoints.GetValue(context));
        PreparePointCamera(context);

        UpdateHoldingStatus();
    }

    /// <summary>
    /// Latches the camera for the next merge: with a camera connected (DepthAnything's CamReference
    /// implements ICamera) PointCloud unprojects into the camera's world space; without one it keeps
    /// the DA3-style camera space with +Z forward that DepthToPoints expects. FieldOfView stays the
    /// fallback frustum when no camera is connected.
    /// </summary>
    private void PreparePointCamera(EvaluationContext context)
    {
        _pendingCamToWorld = System.Numerics.Matrix4x4.Identity;
        _pendingCamIsPose = false;
        _pendingTanHalfX = MathF.Tan(_pendingFov * MathF.PI / 360f) * (float)_width / Math.Max(_height, 1);
        _pendingTanHalfY = MathF.Tan(_pendingFov * MathF.PI / 360f);

        var camObj = CameraReference.GetValue(context);
        if (camObj is ICamera camera)
        {
            var worldToCamera = camera.WorldToCamera;
            var clip = camera.CameraToClipSpace;
            if (worldToCamera != default
                && clip.M11 > 1e-6f && clip.M22 > 1e-6f
                && System.Numerics.Matrix4x4.Invert(worldToCamera, out var camToWorld))
            {
                _pendingCamToWorld = camToWorld;
                _pendingTanHalfX = 1f / clip.M11;
                _pendingTanHalfY = 1f / clip.M22;
                _pendingCamIsPose = true;
            }
        }
    }

    /// <summary>
    /// Tries to map a pending staging copy without blocking. Returns false while the copy is still
    /// in flight. A copy that never retires (device reset) is dropped after a timeout.
    /// </summary>
    private bool TryMapStaging(ref bool pending, T3.Graphics.Compat.Texture2D? staging, ref int waitFrames)
    {
        if (staging is not { IsDisposed: false } || staging.Native == null)
        {
            pending = false;
            return true;
        }

        if (!ResourceManager.Device.Backend.TryMapForRead(staging.Native, 0, out var mapped))
        {
            waitFrames++;
            if (waitFrames <= ReadbackTimeoutFrames)
                return false;

            pending = false;
            SetStatus("A depth copy never completed; the held scan is untouched.", IStatusProvider.StatusLevel.Warning);
            return true;
        }

        if (ReferenceEquals(staging, _depthStaging))
            _mappedDepth = mapped;
        else if (ReferenceEquals(staging, _confStaging))
            _mappedConf = mapped;
        else
            _mappedColor = mapped;
        return true;
    }

    /// <summary>Merges one completed readback into the held scan and uploads the result.</summary>
    private unsafe void MergeAndUpload(bool confMapped, bool colorMapped)
    {
        var mappedDepth = _mappedDepth;
        if (_width == 0 || _heldDepth == null || mappedDepth is not { SizeInBytes: > 0 })
            return;

        var depthView = mappedDepth.Value;

        var width = _width;
        var height = _height;
        var mode = _pendingMode;
        var threshold = _pendingThreshold;
        var hasConfidence = confMapped && _mappedConf is { SizeInBytes: > 0 };
        var hasColor = colorMapped && _mappedColor is { SizeInBytes: > 0 };
        var held = _heldDepth;
        var count = _heldCount;
        var mask = _heldMask;
        var colorSums = _heldColorSum;
        var display = _heldColorDisplay;

        var now = (float)_scanClock.Elapsed.TotalSeconds;
        MappedMemory mappedConf = hasConfidence ? _mappedConf.Value : default;
        MappedMemory mappedColor = hasColor ? _mappedColor.Value : default;

        var total = width * height;
        long validPixels = 0;
        var tmin = float.MaxValue;
        var tmax = float.MinValue;
        var sync = new object();
        Parallel.For(0, height,
                     () => (0L, float.MaxValue, float.MinValue),
                     (y, _, local) =>
                     {
                         var row = (float*)((byte*)depthView.Data + (long)y * depthView.RowPitch);
                         var confRow = hasConfidence
                                           ? (float*)((byte*)mappedConf.Data + (long)y * mappedConf.RowPitch)
                                           : null;

                         var colorRow = hasColor
                                            ? (byte*)((byte*)mappedColor.Data + (long)y * mappedColor.RowPitch)
                                            : null;

                         var baseIndex = y * width;
                         for (var x = 0; x < width; x++)
                         {
                             var d = row[x];
                             if (!float.IsFinite(d) || d <= 0f)
                                 continue;

                             if (confRow != null && confRow[x] < threshold)
                                 continue;

                             var i = baseIndex + x;
                             float r = 1f, g = 1f, b = 1f;
                             if (colorRow != null)
                             {
                                 var c = colorRow + x * 4;
                                 r = c[0] / 255f;
                                 g = c[1] / 255f;
                                 b = c[2] / 255f;
                             }

                             if (count[i] == 0 || mode == ScanMode.Latest)
                             {
                                 held[i] = d;
                                 count[i] = 1;
                                 colorSums[i * 3] = r;
                                 colorSums[i * 3 + 1] = g;
                                 colorSums[i * 3 + 2] = b;
                             }
                             else

                             switch (mode)
                             {
                                 case ScanMode.Average:
                                     held[i] += (d - held[i]) / (count[i] + 1);
                                     colorSums[i * 3] += r;
                                     colorSums[i * 3 + 1] += g;
                                     colorSums[i * 3 + 2] += b;
                                     count[i]++;
                                     break;
                                 case ScanMode.Min:
                                     if (d < held[i])
                                     {
                                         held[i] = d;
                                         colorSums[i * 3] = r;
                                         colorSums[i * 3 + 1] = g;
                                         colorSums[i * 3 + 2] = b;
                                     }
                                     count[i]++;
                                     break;
                                 case ScanMode.Max:
                                     if (d > held[i])
                                     {
                                         held[i] = d;
                                         colorSums[i * 3] = r;
                                         colorSums[i * 3 + 1] = g;
                                         colorSums[i * 3 + 2] = b;
                                     }
                                     count[i]++;
                                     break;
                             }

                             if (count[i] > 0)
                             {
                                 mask[i] = 255;
                                 _lastSeenTime![i] = now;
                                 var k = count[i];
                                 var inv = mode == ScanMode.Average ? 1f / k : 1f;
                                 display[i * 4] = ToByte(colorSums[i * 3] * inv);
                                 display[i * 4 + 1] = ToByte(colorSums[i * 3 + 1] * inv);
                                 display[i * 4 + 2] = ToByte(colorSums[i * 3 + 2] * inv);
                                 display[i * 4 + 3] = 255;
                                 local = (local.Item1 + 1,
                                          Math.Min(local.Item2, held[i]),
                                          Math.Max(local.Item3, held[i]));
                             }
                             else
                             {
                                 mask[i] = 0;
                                 display[i * 4] = 0;
                                 display[i * 4 + 1] = 0;
                                 display[i * 4 + 2] = 0;
                                 display[i * 4 + 3] = 0;
                             }
                         }

                         return local;
                     },
                     local =>
                     {
                         lock (sync)
                         {
                             validPixels += local.Item1;
                             tmin = Math.Min(tmin, local.Item2);
                             tmax = Math.Max(tmax, local.Item3);
                         }
                     });

        if (validPixels == 0)
        {
            SetStatus("The latest capture had no valid depth samples; the held scan is unchanged.", IStatusProvider.StatusLevel.Warning);
            return;
        }

        _heldMin = tmin;
        _heldMax = tmax;
        _coverage = total > 0 ? validPixels / (float)total : 0f;
        _captures++;

        UploadHeldTextures();
        HeldDepth.Value = _heldTexture;
        MaskTexture.Value = _maskTexture;
        HeldColor.Value = _heldColorTexture;
        Coverage.Value = _coverage;
        Captures.Value = _captures;
        RebuildPoints();
        UpdateHoldingStatus();
    }

    private unsafe void UploadHeldTextures()
    {
        if (_heldTexture == null || _maskTexture == null)
            return;

        fixed (float* heldPtr = _heldDepth)
        {
            ResourceManager.Device.ImmediateContext.UpdateSubresource(
                new DataBox((IntPtr)heldPtr, _width * sizeof(float), 0), _heldTexture);
        }

        fixed (byte* maskPtr = _heldMask)
        {
            ResourceManager.Device.ImmediateContext.UpdateSubresource(
                new DataBox((IntPtr)maskPtr, _width, 0), _maskTexture);
        }

        if (_heldColorTexture != null)
        {
            fixed (byte* colorPtr = _heldColorDisplay)
            {
                ResourceManager.Device.ImmediateContext.UpdateSubresource(
                    new DataBox((IntPtr)colorPtr, _width * 4, 0), _heldColorTexture);
            }
        }
    }

    private void AllocateBuffers(int width, int height)
    {
        _width = width;
        _height = height;
        _heldDepth = new float[width * height];
        _heldCount = new int[width * height];
        _heldMask = new byte[width * height];
        _heldColorSum = new float[width * height * 3];
        _heldColorDisplay = new byte[width * height * 4];
        _lastSeenTime = new float[width * height];
        _captures = 0;
        _coverage = 0f;
        _heldMin = 0f;
        _heldMax = 0f;
        _prevPositions = null;
        _prevLayout = 0;
        _mappedDepth = null;
        _mappedConf = null;
        _depthPending = false;
        _confPending = false;
        _colorPending = false;
        _confWaitFrames = 0;
        _colorWaitFrames = 0;

        _heldTexture?.Dispose();
        _heldTexture = null;
        _maskTexture?.Dispose();
        _maskTexture = null;
        _heldColorTexture?.Dispose();
        _heldColorTexture = null;

        _heldTexture = CreateHeldTexture(width, height, Format.R32_Float);
        _maskTexture = CreateHeldTexture(width, height, Format.R8_UNorm);
        _heldColorTexture = CreateHeldTexture(width, height, Format.R8G8B8A8_UNorm);

        HeldDepth.Value = null;
        MaskTexture.Value = null;
        HeldColor.Value = null;
        PointCloud.Value = null;
        PointCount.Value = 0;
        Coverage.Value = 0;
        Captures.Value = 0;
    }

    private static Texture2D CreateHeldTexture(int width, int height, Format format)
    {
        var description = new Texture2DDescription
        {
            Width = width,
            Height = height,
            MipLevels = 1,
            ArraySize = 1,
            Format = format,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
            OptionFlags = ResourceOptionFlags.None
        };
        return new Texture2D(new T3.Graphics.Compat.Texture2D(ResourceManager.Device, description));
    }

    /// <summary>One staging texture per source; replaced on size change so buffers don't accumulate.</summary>
    private void GetOrCreateStaging(int width, int height)
    {
        if (_depthStaging is { IsDisposed: false }
            && _depthStaging.Description.Width == width
            && _depthStaging.Description.Height == height)
            return;

        _depthPending = false;
        _confPending = false;
        _depthStaging?.Dispose();
        _confStaging?.Dispose();
        _depthStaging = CreateStagingTexture(width, height, Format.R32_Float);
        _confStaging = CreateStagingTexture(width, height, Format.R32_Float);
        _colorStaging = CreateStagingTexture(width, height, Format.R8G8B8A8_UNorm);
    }

    private static T3.Graphics.Compat.Texture2D CreateStagingTexture(int width, int height, Format format)
    {
        var description = new Texture2DDescription
        {
            Width = width,
            Height = height,
            MipLevels = 1,
            ArraySize = 1,
            Format = format,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CpuAccessFlags = CpuAccessFlags.Read,
            OptionFlags = ResourceOptionFlags.None
        };
        return new T3.Graphics.Compat.Texture2D(ResourceManager.Device, description);
    }

    private void HandleReset(EvaluationContext context)
    {
        if (!MathUtils.WasTriggered(Reset.GetValue(context), ref _resetState))
            return;

        Reset.SetTypedInputValue(false);
        _resetState = false;
        ResetHeldScan();
        SetStatus("The held scan was reset.", IStatusProvider.StatusLevel.Notice);
    }

    private void HandleRecordTriggers(EvaluationContext context)
    {
        if (MathUtils.WasTriggered(StartRecord.GetValue(context), ref _startRecordState))
        {
            StartRecord.SetTypedInputValue(false);
            _startRecordState = false;
            _recording = true;
        }

        if (MathUtils.WasTriggered(StopRecord.GetValue(context), ref _stopRecordState))
        {
            StopRecord.SetTypedInputValue(false);
            _stopRecordState = false;
            _recording = false;
        }
    }

    private void ResetHeldScan()
    {
        if (_width == 0)
            return;

        Array.Clear(_heldDepth);
        Array.Clear(_heldCount);
        Array.Clear(_heldMask);
        Array.Clear(_heldColorSum);
        Array.Clear(_heldColorDisplay);
        _captures = 0;
        _coverage = 0f;
        _heldMin = 0f;
        _heldMax = 0f;
        HeldDepth.Value = null;
        MaskTexture.Value = null;
        HeldColor.Value = null;
        PointCloud.Value = null;
        PointCount.Value = 0;
        Coverage.Value = 0;
        Captures.Value = 0;
    }

    /// <summary>
    /// Ages samples out once their lifetime is over: pixels not re-observed within SampleLifetime
    /// seconds are dropped, so a scan fades the way a LiDAR sweep decays. Returns true when anything
    /// expired (outputs were refreshed).
    /// </summary>
    private bool ExpireOldSamples(float lifetime)
    {
        var now = (float)_scanClock.Elapsed.TotalSeconds;
        var count = _heldCount!;
        var held = _heldDepth!;
        var mask = _heldMask!;
        var display = _heldColorDisplay!;
        var lastSeen = _lastSeenTime!;
        var expired = 0L;
        var tmin = float.MaxValue;
        var tmax = float.MinValue;
        var sync = new object();
        Parallel.For(0, _height,
                     () => (0L, float.MaxValue, float.MinValue),
                     (y, _, local) =>
                     {
                         var row = y * _width;
                         for (var x = 0; x < _width; x++)
                         {
                             var i = row + x;
                             if (count[i] > 0 && now - lastSeen[i] > lifetime)
                             {
                                 count[i] = 0;
                                 held[i] = 0f;
                                 mask[i] = 0;
                                 display[i * 4] = 0;
                                 display[i * 4 + 1] = 0;
                                 display[i * 4 + 2] = 0;
                                 display[i * 4 + 3] = 0;
                                 local = (local.Item1 + 1, local.Item2, local.Item3);
                                 continue;
                             }

                             if (count[i] > 0)
                             {
                                 local = (local.Item1, Math.Min(local.Item2, held[i]), Math.Max(local.Item3, held[i]));
                             }
                         }

                         return local;
                     },
                     local =>
                     {
                         lock (sync)
                         {
                             expired += local.Item1;
                             tmin = Math.Min(tmin, local.Item2);
                             tmax = Math.Max(tmax, local.Item3);
                         }
                     });

        if (expired == 0)
            return false;

        _heldMin = tmin == float.MaxValue ? 0f : tmin;
        _heldMax = tmax == float.MinValue ? 0f : tmax;
        _coverage = CountValidPixels() / (float)(_width * _height);
        UploadHeldTextures();
        HeldDepth.Value = _heldTexture;
        MaskTexture.Value = _maskTexture;
        HeldColor.Value = _heldColorTexture;
        Coverage.Value = _coverage;
        RebuildPoints();
        UpdateHoldingStatus();
        return true;
    }

    private void HandleSave(EvaluationContext context)
    {
        if (!MathUtils.WasTriggered(SaveTrigger.GetValue(context), ref _saveState))
            return;

        SaveTrigger.SetTypedInputValue(false);
        _saveState = false;

        if (_width == 0 || _captures == 0)
        {
            SetStatus("No scan held to save; capture depth frames first.", IStatusProvider.StatusLevel.Warning);
            SavedPath.Value = string.Empty;
            return;
        }

        var format = (SaveFormat)Math.Clamp(SaveFormatInput.GetValue(context), 0, 1);
        var scale = Math.Max(0f, SaveScale.GetValue(context));
        var relativePath = BuildRelativePath(SaveFolder.GetValue(context) ?? string.Empty,
                                             SaveFileName.GetValue(context) ?? string.Empty,
                                             format == SaveFormat.Png16Bit ? ".png" : ".raw");
        if (relativePath.Length == 0)
        {
            SetStatus("The file name is empty; the scan was not saved.", IStatusProvider.StatusLevel.Warning);
            SavedPath.Value = string.Empty;
            return;
        }

        if (!AssetRegistry.TryResolveAddressForWriting(relativePath, this, out var absolutePath, out var failureReason))
        {
            SetStatus($"The scan could not be written: {failureReason}", IStatusProvider.StatusLevel.Error);
            SavedPath.Value = string.Empty;
            Log.Warning($"[DepthScanBuffer] {failureReason}", this);
            return;
        }

        try
        {
            if (format == SaveFormat.Png16Bit)
                WritePng16Bit(absolutePath, scale);
            else
                WriteRawFloat32(absolutePath);

            SavedPath.Value = relativePath;
            SetStatus($"Saved {_width}x{_height} scan ({_captures} captures) to {relativePath}", IStatusProvider.StatusLevel.Success);
        }
        catch (Exception e)
        {
            SavedPath.Value = string.Empty;
            SetStatus($"Saving the scan failed: {e.Message}", IStatusProvider.StatusLevel.Error);
            Log.Error($"[DepthScanBuffer] Saving {absolutePath} failed: {e.Message}", this);
        }
    }

    private static string BuildRelativePath(string folder, string fileName, string extension)
    {
        var name = SanitizeFileName(fileName, extension);
        if (name.Length == 0)
            return string.Empty;

        var dir = folder.Trim();
        if (dir.Length == 0)
            return name;

        dir = dir.Replace('\\', '/').TrimEnd('/');
        return $"{dir}/{name}";
    }

    /// <summary>
    /// Reduces the file name to one safe segment that always ends in the given extension, so a
    /// stray separator can never redirect the write out of the folder.
    /// </summary>
    private static string SanitizeFileName(string fileName, string extension)
    {
        var name = (fileName ?? string.Empty).Trim();

        foreach (var invalidChar in Path.GetInvalidFileNameChars())
            name = name.Replace(invalidChar, '_');

        name = name.TrimEnd('.', ' ');
        if (name.Length == 0)
            return string.Empty;

        var lastDot = name.LastIndexOf('.');
        if (lastDot > 0)
            name = name[..lastDot];

        return name + extension;
    }

    /// <summary>
    /// Writes the held scan as a 16-bit grayscale PNG: valid pixels store Clamp(depth * scale,
    /// 0..65535), empty pixels store 0. The scale maps model units (meters for DA3) to integers,
    /// e.g. 1000 for millimeters.
    /// </summary>
    private void WritePng16Bit(string absolutePath, float scale)
    {
        var width = _width;
        var height = _height;
        var depth = _heldDepth;
        var count = _heldCount;

        var raw = new byte[(width * 2 + 1) * height];
        var rowStart = 0;
        for (var y = 0; y < height; y++)
        {
            raw[rowStart] = 0; // filter type 0: none
            for (var x = 0; x < width; x++)
            {
                var i = y * width + x;
                var v = count[i] > 0 ? depth[i] * scale : 0f;
                v = Math.Clamp(v, 0f, 65535f);
                var value16 = (ushort)(v + 0.5f);
                raw[rowStart + 1 + x * 2] = (byte)(value16 >> 8);
                raw[rowStart + 2 + x * 2] = (byte)(value16 & 0xFF);
            }

            rowStart += width * 2 + 1;
        }
        using (var file = File.Create(absolutePath))
        {

        var ihdr = new byte[13];
        ihdr[0] = (byte)(width >> 24);
        ihdr[1] = (byte)(width >> 16);
        ihdr[2] = (byte)(width >> 8);
        ihdr[3] = (byte)width;
        ihdr[4] = (byte)(height >> 24);
        ihdr[5] = (byte)(height >> 16);
        ihdr[6] = (byte)(height >> 8);
        ihdr[7] = (byte)height;
        ihdr[8] = 16;
        ihdr[9] = 0;
        ihdr[10] = 0;
        ihdr[11] = 0;
        ihdr[12] = 0;

        WritePngChunk(file, "IHDR", ihdr);

        var idat = new MemoryStream();
        var zlib = new ZLibStream(idat, CompressionLevel.Fastest, true);
        zlib.Write(raw);
        zlib.Dispose();
        idat.Position = 0;
        WritePngChunk(file, "IDAT", idat.ToArray());
        WritePngChunk(file, "IEND", []);

        }
    }

    private static void WritePngChunk(Stream file, string type, byte[] data)
    {
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        WriteBigEndianInt(file, data.Length);
        file.Write(typeBytes, 0, 4);
        file.Write(data, 0, data.Length);
        WriteBigEndianInt(file, (int)Crc32(typeBytes, data));
    }

    private static void WriteBigEndianInt(Stream file, int value)
    {
        file.WriteByte((byte)(value >> 24));
        file.WriteByte((byte)(value >> 16));
        file.WriteByte((byte)(value >> 8));
        file.WriteByte((byte)value);
    }

    private static uint Crc32(byte[] typeBytes, byte[] data)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (var b in typeBytes)
        {
            crc ^= b;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ (0xEDB88320u & ~(crc & 1));
        }

        foreach (var b in data)
        {
            crc ^= b;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ (0xEDB88320u & ~(crc & 1));
        }

        return crc ^ 0xFFFFFFFFu;
    }

    /// <summary>
    /// Writes the held scan as raw float32 little-endian, row-major, invalid pixels as 0, behind a
    /// small header so a later load function can read it back exactly.
    /// </summary>
    private void WriteRawFloat32(string absolutePath)
    {
        var width = _width;
        var height = _height;

        using (var file = File.Create(absolutePath))
        {
            using (var writer = new BinaryWriter(file))
            {
                writer.Write(new[] { (byte)'T', (byte)'3', (byte)'D', (byte)'S' });
                writer.Write(1);          // version
                writer.Write(width);
                writer.Write(height);
                writer.Write(_captures);
                writer.Write((int)CountValidPixels());
                writer.Write(_heldMin);
                writer.Write(_heldMax);
                var held = _heldDepth;
                var count = _heldCount;
                for (var i = 0; i < width * height; i++)
                    writer.Write(count[i] > 0 ? held[i] : 0f);
            }
        }
    }

    private long CountValidPixels()
    {
        var count = _heldCount;
        var total = 0L;
        for (var i = 0; i < count.Length; i++)
        {
            if (count[i] > 0)
                total++;
        }

        return total;
    }

    private void UpdateHoldingStatus()
    {
        if (_captures == 0)
        {
            SetStatus("Enable Capture to merge depth frames into the held scan.", IStatusProvider.StatusLevel.Notice);
            return;
        }

        var maxCaptures = _uiMaxCaptures;
        var complete = maxCaptures > 0 && _captures >= maxCaptures;
        if (complete)
        {
            SetStatus($"Scan complete: {_captures} captures, {_coverage:P0} coverage. Save it with the Save input.", IStatusProvider.StatusLevel.Success);
            return;
        }

        var state = _recording ? "Recording" : "Holding";
        SetStatus($"{state} {_width}x{_height} scan: {_captures} captures, {_coverage:P0} coverage.", IStatusProvider.StatusLevel.Success);
    }

    private void SetStatus(string message, IStatusProvider.StatusLevel level)
    {
        _statusMessage = message;
        _statusLevel = level;
        if (level >= IStatusProvider.StatusLevel.Warning && message != _lastLoggedStatus)
        {
            _lastLoggedStatus = message;
            Log.Warning($"[DepthScanBuffer] {message}", this);
        }
    }

    private static byte ToByte(float value)
    {
        return (byte)Math.Clamp((int)(value * 255f + 0.5f), 0, 255);
    }

    /// <summary>
    /// Rebuilds the CPU point cloud from the held scan. CPU-uploaded point buffers draw on both
    /// backends, unlike the compute-written ones from DepthToPoints (D3D11 renders those black).
    /// </summary>
    private void RebuildPoints()
    {
        var hex = _uiPointGrid == (int)PointGridMode.Hexagonal;
        var stride = _pendingStride;
        var maxPoints = _pendingMaxPoints;
        ComputeGrid(_width, _height, hex, ref stride, maxPoints, out var gridWidth, out var gridHeight, out var yStep);

        var pointCount = gridWidth * gridHeight;
        if (pointCount <= 0)
        {
            PointCloud.Value = null;
            PointCount.Value = 0;
            return;
        }

        var points = new Point[pointCount];
        var held = _heldDepth;
        var count = _heldCount;
        var stability = _uiStability;
        var blend = stability > 0f;
        var layout = HashCode.Combine(_width, _height, stride, hex, gridWidth, gridHeight, yStep, _pendingCamIsPose);
        if (blend && (_prevPositions == null || _prevLayout != layout))
        {
            _prevPositions = new System.Numerics.Vector3[pointCount];
            _prevLayout = layout;
        }
        var prev = blend && _prevLayout == layout && _prevPositions!.Length == pointCount ? _prevPositions : null;
        var write = blend && _prevPositions != null && _prevLayout == layout ? _prevPositions : null;

        for (var gy = 0; gy < gridHeight; gy++)
        {
            var py = Math.Min(gy * yStep, _height - 1);
            var offset = hex && (gy & 1) == 1 ? stride / 2 : 0;
            for (var gx = 0; gx < gridWidth; gx++)
            {
                var px = Math.Min(gx * stride + offset, _width - 1);
                var i = py * _width + px;
                var idx = gy * gridWidth + gx;
                var p = new Point { F1 = 1, F2 = 1 };
                p.Orientation = System.Numerics.Quaternion.Identity;
                p.Scale = System.Numerics.Vector3.One;
                p.Color = System.Numerics.Vector4.One;
                if (count[i] == 0)
                {
                    p.Position = System.Numerics.Vector3.Zero;
                    p.Color = System.Numerics.Vector4.Zero;
                    p.Scale = System.Numerics.Vector3.Zero;
                    if (write != null)
                        write[idx] = System.Numerics.Vector3.Zero;
                    points[idx] = p;
                    continue;
                }

                var z = held[i] * _pendingScale;
                var u = (px + 0.5f) / _width;
                var v = (py + 0.5f) / _height;
                var ndcX = u * 2f - 1f;
                var ndcY = 1f - v * 2f;
                System.Numerics.Vector3 position;
                if (_pendingCamIsPose)
                {
                    // tixl cameras are right-handed and look down -Z, so flip the depth sign.
                    var camPoint = new System.Numerics.Vector3(ndcX * _pendingTanHalfX * z, ndcY * _pendingTanHalfY * z, -z);
                    position = System.Numerics.Vector3.Transform(camPoint, _pendingCamToWorld);
                }
                else
                {
                    position = new System.Numerics.Vector3(ndcX * _pendingTanHalfX * z, ndcY * _pendingTanHalfY * z, z);
                }
                if (prev != null)
                    position = System.Numerics.Vector3.Lerp(prev[idx], position, 1f - stability);
                p.Position = position;
                if (write != null)
                    write[idx] = position;
                var colorIdx = i * 4;
                p.Color = new System.Numerics.Vector4(
                    _heldColorDisplay[colorIdx] / 255f,
                    _heldColorDisplay[colorIdx + 1] / 255f,
                    _heldColorDisplay[colorIdx + 2] / 255f, 1f);
                points[idx] = p;
            }
        }

        EnsurePointBuffer(points, pointCount);
        PointCloud.Value = _pointBuffer;
        PointCount.Value = pointCount;
    }

    private void EnsurePointBuffer(Point[] points, int count)
    {
        var bytesNeeded = count * Point.Stride;
        var currentCount = _pointBuffer?.Buffer != null
                               ? _pointBuffer.Buffer.Description.SizeInBytes / Point.Stride
                               : 0;
        if (count != currentCount)
        {
            _pointBuffer?.Dispose();
            _pointBuffer = null;
        }

        if (_pointBuffer == null)
        {
            _pointBuffer = new BufferWithViews();
            ResourceManager.SetupStructuredBuffer(points, bytesNeeded, Point.Stride, ref _pointBuffer.Buffer);
            ResourceManager.CreateStructuredBufferSrv(_pointBuffer.Buffer, ref _pointBuffer.Srv);
            ResourceManager.CreateStructuredBufferUav(_pointBuffer.Buffer, UnorderedAccessViewBufferFlags.None, ref _pointBuffer.Uav);
            return;
        }

        ResourceManager.Device.ImmediateContext.UpdateSubresource(points, _pointBuffer.Buffer!);
    }

    /// <summary>
    /// Keeps the point count under maxPoints by growing the stride (from DepthToPoints). Hexagonal
    /// mode spaces rows by stride * sqrt(3)/2 and offsets every other row by half a stride, so all
    /// six neighbors sit at the same distance.
    /// </summary>
    private static void ComputeGrid(int width, int height, bool hex, ref int stride, int maxPoints,
                                    out int gridWidth, out int gridHeight, out int yStep)
    {
        while (true)
        {
            yStep = hex ? Math.Max(1, (int)MathF.Round(stride * 0.8660254f)) : stride;
            gridWidth = (width + stride - 1) / stride;
            gridHeight = (height + yStep - 1) / yStep;
            if ((long)gridWidth * gridHeight <= maxPoints || stride >= 64)
                return;

            stride++;
        }
    }

    string? ICustomDropdownHolder.GetValueForInput(Guid inputId)
    {
        if (inputId == Mode.Id)
        {
            var index = Math.Clamp(_uiMode, 0, ModeNames.Length - 1);
            return ModeNames[index];
        }

        if (inputId == SaveFormatInput.Id)
        {
            var index = Math.Clamp(_uiSaveFormat, 0, SaveFormatNames.Length - 1);
            return SaveFormatNames[index];
        }

        return "";
    }

    IEnumerable<string> ICustomDropdownHolder.GetOptionsForInput(Guid inputId)
    {
        if (inputId == Mode.Id)
        {
            foreach (var name in ModeNames)
                yield return name;
        }

        if (inputId == SaveFormatInput.Id)
        {
            foreach (var name in SaveFormatNames)
                yield return name;
        }

        yield break;
    }

    void ICustomDropdownHolder.HandleResultForInput(Guid inputId, string? selected, bool isAListItem)
    {
        if (inputId == Mode.Id)
        {
            var index = Array.IndexOf(ModeNames, selected);
            if (index >= 0)
                Mode.SetTypedInputValue(index);
        }

        if (inputId == SaveFormatInput.Id)
        {
            var index = Array.IndexOf(SaveFormatNames, selected);
            if (index >= 0)
                SaveFormatInput.SetTypedInputValue(index);
        }
    }

    /// <summary>How new depth frames merge into the held scan; values only ever appended.</summary>
    public enum ScanMode
    {
        Latest,
        Average,
        Min,
        Max,
    }

    /// <summary>File format for saving the held scan; values only ever appended.</summary>
    public enum SaveFormat
    {
        Png16Bit,
        RawFloat32,
    }

    /// <summary>Point cloud sampling grid; values only ever appended.</summary>
    public enum PointGridMode
    {
        Square,
        Hexagonal,
    }

    #region Inputs

    [Input(Guid = "37d9f547-32f0-422a-8d4e-5a6b8ca8b81a")]
    public readonly InputSlot<Texture2D?> DepthMap = new();

    [Input(Guid = "f11ea5dc-2f47-4fb4-a9c5-174d0d9b7e22")]
    public readonly InputSlot<Texture2D?> Confidence = new();

    [Input(Guid = "e6a1b2c3-d4e5-4f60-a7b8-c9d0e1f2a3b4")]
    public readonly InputSlot<Texture2D?> ColorMap = new();

    [Input(Guid = "dc882e31-4963-4c64-8a56-1cca9af6b362")]
    public readonly InputSlot<bool> Capture = new(false);

    [Input(Guid = "4713556d-3b2d-4f7e-bbd1-5f15b70784ff")]
    public readonly InputSlot<bool> CaptureOnce = new(false);

    [Input(Guid = "5e4d5335-b71c-4f19-b520-1dcd33132a01")]
    public readonly InputSlot<bool> Reset = new(false);

    [Input(Guid = "f7a8b9c0-d1e2-4a3b-8c5d-6e7f8a9b0c1d")]
    public readonly InputSlot<bool> SaveTrigger = new(false);

    [Input(Guid = "f84cf1ce-6b69-4374-bd24-21e83332cfbc", MappedType = typeof(ScanMode))]
    public readonly InputSlot<int> Mode = new((int)ScanMode.Average);

    [Input(Guid = "883b1806-aad6-4287-b5c8-c33557b440c4")]
    public readonly InputSlot<int> MaxCaptures = new(0);

    [Input(Guid = "b54e0fb0-b37f-4bd7-b12b-fa5315686960")]
    public readonly InputSlot<float> ConfidenceThreshold = new(0.5f);

    [Input(Guid = "9e5e441e-b065-4083-a50e-3fa731157d26")]
    public readonly InputSlot<float> SaveScale = new(1000f);

    [Input(Guid = "ec25952d-6e85-4e37-b65a-a682d8d95580")]
    public readonly InputSlot<string> SaveFolder = new("Resources/DepthScans");

    [Input(Guid = "978bd764-3d64-4a0f-b00c-684da15daab4")]
    public readonly InputSlot<string> SaveFileName = new("depth-scan");

    [Input(Guid = "23182ac9-0259-46db-ae29-e73f90aa24e6", MappedType = typeof(SaveFormat))]
    public readonly InputSlot<int> SaveFormatInput = new((int)SaveFormat.Png16Bit);

    [Input(Guid = "a1f2e3d4-c5b6-4a7c-8d9e-0f1a2b3c4d5e")]
    public readonly InputSlot<float> FieldOfView = new(50f);

    [Input(Guid = "c8d9e0f1-a2b3-4c5d-8e6f-7a8b9c0d1e2f")]
    public readonly InputSlot<object> CameraReference = new();

    [Input(Guid = "b2c3d4e5-f6a7-4b8c-9d0e-1f2a3b4c5d6f")]
    public readonly InputSlot<float> PointScale = new(1f);

    [Input(Guid = "c3d4e5f6-a7b8-4c9d-0e1f-2a3b4c5d6e7f")]
    public readonly InputSlot<int> PixelStride = new(2);

    [Input(Guid = "d4e5f6a7-b8c9-4d0e-a1f2-3b4c5d6e7f8a")]
    public readonly InputSlot<int> MaxPoints = new(262144);

    [Input(Guid = "6e1f7081-92a3-4e1f-8a3b-4c5d6e7f8a9b")]
    public readonly InputSlot<bool> StartRecord = new(false);

    [Input(Guid = "7f2a8192-a3b4-4f2a-9b4c-5d6e7f8a9b0c")]
    public readonly InputSlot<bool> StopRecord = new(false);

    [Input(Guid = "8a3b92a3-b4c5-4a3b-8c5d-6e7f8a9b0c1d")]
    public readonly InputSlot<float> SampleLifetime = new(0f);

    [Input(Guid = "9b4ca3b4-c5d6-4b4c-9d6e-7f8a9b0c1d2e", MappedType = typeof(PointGridMode))]
    public readonly InputSlot<int> PointGrid = new((int)PointGridMode.Square);

    [Input(Guid = "ac5db4c5-d6e7-4c5d-ae7f-8a9b0c1d2e3f")]
    public readonly InputSlot<float> PointStability = new(0f);

    #endregion

    #region Private Fields

    private const int ReadbackTimeoutFrames = 60;

    private float[]? _heldDepth;
    private int[]? _heldCount;
    private byte[]? _heldMask;
    private float[]? _heldColorSum;
    private byte[]? _heldColorDisplay;
    private int _width;
    private int _height;
    private int _captures;
    private float _coverage;
    private float _heldMin;
    private float _heldMax;

    private Texture2D? _heldTexture;
    private Texture2D? _maskTexture;
    private Texture2D? _heldColorTexture;
    private T3.Graphics.Compat.Texture2D? _depthStaging;
    private T3.Graphics.Compat.Texture2D? _confStaging;
    private T3.Graphics.Compat.Texture2D? _colorStaging;
    private MappedMemory? _mappedDepth;
    private MappedMemory? _mappedConf;
    private MappedMemory? _mappedColor;
    private bool _depthPending;
    private bool _confPending;
    private bool _colorPending;
    private int _waitFrames;
    private int _confWaitFrames;
    private int _colorWaitFrames;

    private System.Numerics.Matrix4x4 _pendingCamToWorld;
    private float _pendingTanHalfX;
    private float _pendingTanHalfY;
    private bool _pendingCamIsPose;
    private ScanMode _pendingMode;
    private float _pendingThreshold;
    private float _pendingFov;
    private float _pendingScale;
    private int _pendingStride;
    private int _pendingMaxPoints;
    private BufferWithViews? _pointBuffer;
    private bool _captureOnceState;
    private bool _resetState;
    private bool _saveState;
    private bool _startRecordState;
    private bool _stopRecordState;
    private bool _loggedWrongFormat;
    private bool _loggedConfMismatch;
    private bool _loggedColorMismatch;
    private string? _statusMessage;
    private IStatusProvider.StatusLevel _statusLevel;
    private string? _lastLoggedStatus;
    private int _uiMode;
    private int _uiSaveFormat;
    private int _uiMaxCaptures;
    private int _uiPointGrid;
    private float _uiStability;
    private bool _recording;
    private readonly System.Diagnostics.Stopwatch _scanClock = System.Diagnostics.Stopwatch.StartNew();
    private float[]? _lastSeenTime;
    private System.Numerics.Vector3[]? _prevPositions;
    private int _prevLayout;

    #endregion

    public IStatusProvider.StatusLevel GetStatusLevel()
    {
        return _statusLevel;
    }

    public string? GetStatusMessage()
    {
        return _statusMessage;
    }

    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing)
            return;

        _heldTexture?.Dispose();
        _heldTexture = null;
        _maskTexture?.Dispose();
        _maskTexture = null;
        _heldColorTexture?.Dispose();
        _heldColorTexture = null;
        _depthStaging?.Dispose();
        _depthStaging = null;
        _confStaging?.Dispose();
        _confStaging = null;
        _colorStaging?.Dispose();
        _colorStaging = null;
        _pointBuffer?.Dispose();
        _pointBuffer = null;
        base.Dispose(isDisposing);
    }

    private static readonly string[] ModeNames = { "Latest", "Average", "Min", "Max" };
    private static readonly string[] SaveFormatNames = { "16-bit PNG", "Raw Float" };
}
