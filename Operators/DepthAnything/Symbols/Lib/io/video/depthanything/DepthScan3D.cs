
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
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

[Guid("7e6eb18c-433f-41c0-8443-6a82dae72703")]
public sealed class DepthScan3D : Instance<DepthScan3D>, IStatusProvider, ICustomDropdownHolder
{
    #region Outputs

    [Output(Guid = "9165831a-1b50-44f2-a5e3-c88c5e630423", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<BufferWithViews?> Points = new();

    [Output(Guid = "29fc10f7-e7a9-49a7-abca-62b5105b3914", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> PointCount = new();

    [Output(Guid = "63df221c-b771-430e-bad7-ea252018c1ff", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> Captures = new();

    [Output(Guid = "077366c4-089f-4bfe-ae55-72ec76dbb0be", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> PointsAdded = new();

    [Output(Guid = "9b45f9d2-07d1-4385-b448-9ff857382e8d")]
    public readonly Slot<string> SavedPath = new();

    [Output(Guid = "b19a2b3c-4d5e-4f60-a1b2-c3d4e5f6a7b8", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<bool> Recording = new();

    #endregion

    public DepthScan3D()
    {
        Points.UpdateAction = Update;
        PointCount.UpdateAction = Update;
        Captures.UpdateAction = Update;
        PointsAdded.UpdateAction = Update;
        SavedPath.UpdateAction = Update;
        Recording.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        HandleReset(context);
        HandleRecordTriggers(context);
        if (_meshDone)
        {
            _meshDone = false;
            SavedPath.Value = _meshResultPath;
            SetStatus(_meshResultMessage, _meshResultPath.Length > 0 ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning);
        }

        _pendingGlobalAlign = GlobalAlign.GetValue(context);
        _pendingFlat = FlatSurfaces.GetValue(context);
        _pendingRegularize = RegularizeRoom.GetValue(context);
        _pendingFloorA = Math.Max(0f, KnownFloorSizeA.GetValue(context));
        _pendingFloorB = Math.Max(0f, KnownFloorSizeB.GetValue(context));
        if (_pendingSpherical && _points.Count > 0)
        {
            MaybeStartShell();
            ApplyShellIfReady();
        }

        HandleSave(context);
        Recording.Value = _recording;

        var depthTexture = DepthMap.GetValue(context);
        if (depthTexture == null || depthTexture.IsDisposed)
        {
            _depthPending = false;
            _confPending = false;
            _colorPending = false;
            if (_points.Count == 0)
                SetStatus("Connect a depth map (e.g. DepthAnything's DepthTexture) and enable Capture.", IStatusProvider.StatusLevel.Notice);
            return;
        }

        var desc = depthTexture.Description;
        if (desc.Format != Format.R32_Float)
        {
            if (!_loggedWrongFormat)
            {
                _loggedWrongFormat = true;
                Log.Warning("[DepthScan3D] Unsupported depth format; expected R32_Float.", this);
            }

            SetStatus("Unsupported depth format; connect a R32_Float depth map (DepthAnything's DepthTexture).", IStatusProvider.StatusLevel.Warning);
            return;
        }

        _uiMode = Mode.GetValue(context);
        _uiSaveFormat = SaveFormatInput.GetValue(context);

        var capture = Capture.GetValue(context);
        var captureOnce = MathUtils.WasTriggered(CaptureOnce.GetValue(context), ref _captureOnceState);
        if (captureOnce)
        {
            CaptureOnce.SetTypedInputValue(false);
            _captureOnceState = false;
        }

        var lifetime = Math.Max(0f, SampleLifetime.GetValue(context));
        if (lifetime > 0f && _points.Count > 0 && ExpirePoints(lifetime))
            return;

        // Complete the previous capture's readback and merge it before starting a new one. All copies of a capture
        // must be mapped before merging: the larger color frame often lands a frame after the depth, and merging
        // without it left the points white. Each staging texture is mapped once and unmapped after the merge, since
        // copying into a still-mapped texture fails and later captures read stale memory.
        if (_depthPending)
        {
            if (_mappedDepth == null && !TryMapStaging(ref _depthPending, _depthStaging, ref _waitFrames))
                return;

            if (_confPending && _mappedConf == null && !TryMapStaging(ref _confPending, _confStaging, ref _confWaitFrames))
                return;

            if (_colorPending && _mappedColor == null && !TryMapStaging(ref _colorPending, _colorStaging, ref _colorWaitFrames))
                return;

            MergeScan(_mappedConf != null, _mappedColor != null);
            UnmapStaging();
            _depthPending = false;
            _confPending = false;
            _colorPending = false;

            // Capture ended while the copy was in flight: the merge above was valid, now hold.
            if (!capture && !captureOnce && !_recording)
            {
                UpdateStatus();
                return;
            }
        }

        if (!capture && !captureOnce && !_recording)
        {
            UpdateStatus();
            return;
        }

        if (CameraReference.GetValue(context) is DepthAnything { PoseTrusted: false })
        {
            _skippedLost++;
            SetStatus($"Camera pose lost, capture skipped ({_skippedLost} so far); the held scan is untouched.", IStatusProvider.StatusLevel.Warning);
            return;
        }

        var confidence = Confidence.GetValue(context);
        var useConfidence = confidence is { IsDisposed: false }
                            && confidence.Description.Format == Format.R32_Float
                            && confidence.Description.Width == desc.Width
                            && confidence.Description.Height == desc.Height;
        if (confidence is { IsDisposed: false } && !useConfidence && !_loggedConfMismatch)
        {
            _loggedConfMismatch = true;
            Log.Warning("[DepthScan3D] Confidence texture ignored; size or format differs from the depth map.", this);
        }

        // The color map may be larger than the depth (the camera frame vs. the model's output) and BGRA (video
        // decoders): it is sampled at each depth pixel's position, so only the channel layout has to be known.
        var colorTexture = ColorMap.GetValue(context);
        var colorFormat = colorTexture is { IsDisposed: false } ? colorTexture.Description.Format : Format.Unknown;
        var colorIsBgra = colorFormat is Format.B8G8R8A8_UNorm or Format.B8G8R8A8_UNorm_SRgb;
        var useColor = colorIsBgra || colorFormat is Format.R8G8B8A8_UNorm or Format.R8G8B8A8_UNorm_SRgb;
        if (colorTexture is { IsDisposed: false } && !useColor && !_loggedColorMismatch)
        {
            _loggedColorMismatch = true;
            Log.Warning($"[DepthScan3D] ColorMap ignored; {colorFormat} is not an 8-bit RGBA or BGRA format.", this);
        }

        GetOrCreateStaging(desc.Width, desc.Height);
        var deviceContext = ResourceManager.Device.ImmediateContext;
        deviceContext.CopyResource(depthTexture, _depthStaging);
        _depthPending = true;
        _confPending = false;
        _colorPending = false;

        if (useConfidence)
        {
            deviceContext.CopyResource(confidence, _confStaging);
            _confPending = true;
        }

        if (useColor)
        {
            GetOrCreateColorStaging(colorTexture!.Description.Width, colorTexture.Description.Height, colorFormat);
            _pendingColorIsBgra = colorIsBgra;
            deviceContext.CopyResource(colorTexture, _colorStaging);
            _colorPending = true;
        }

        _waitFrames = 0;
        _confWaitFrames = 0;
        _colorWaitFrames = 0;
        _pendingThreshold = Math.Clamp(ConfidenceThreshold.GetValue(context), 0f, 1f);
        _pendingMode = (MergeMode)Math.Clamp(Mode.GetValue(context), 0, 1);
        _pendingScale = Math.Max(1e-6f, PointScale.GetValue(context));
        _pendingVoxelSize = Math.Max(1e-5f, VoxelSize.GetValue(context));
        _pendingStride = Math.Clamp(PixelStride.GetValue(context), 1, 64);
        _pendingMaxPoints = Math.Max(1000, MaxPoints.GetValue(context));
        _pendingAutoScale = AutoScale.GetValue(context);
        _pendingRegularize = RegularizeRoom.GetValue(context);
        _pendingGlobalAlign = GlobalAlign.GetValue(context);
        _pendingFlat = FlatSurfaces.GetValue(context);
        _pendingFloorA = Math.Max(0f, KnownFloorSizeA.GetValue(context));
        _pendingFloorB = Math.Max(0f, KnownFloorSizeB.GetValue(context));
        PrepareCamera(context, desc.Width, desc.Height);
        UpdateStatus();
    }

    /// <summary>
    /// Sets up the camera-to-world transform and frustum half-tangents for the next merge. With a
    /// camera connected (DepthAnything's CamReference implements ICamera) points land in world space;
    /// without one, points use DA3-style camera space with +Z forward and y up.
    /// </summary>
    private void PrepareCamera(EvaluationContext context, int width, int height)
    {
        _pendingCamToWorld = System.Numerics.Matrix4x4.Identity;
        _pendingCamIsPose = false;
        _pendingSpherical = false;
        var tanHalfY = MathF.Tan(Math.Clamp(FieldOfView.GetValue(context), 1f, 179f) * MathF.PI / 360f);
        var aspect = Math.Max(width, 1) / (float)Math.Max(height, 1);
        _pendingTanHalfX = tanHalfY * aspect;
        _pendingTanHalfY = tanHalfY;

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

                // A camera that only rotates about the origin (a pan/tilt head) sees one surface per direction
                _pendingSpherical = camToWorld.Translation.LengthSquared() < 1e-6f;
            }
        }
    }

    /// <summary>
    /// Merges one completed readback into the world-space scan: every valid pixel is unprojected to
    /// world space and inserted into the voxel hash, where overlapping observations are averaged or
    /// replaced. Runs single-threaded; PixelStride limits the per-capture cost.
    /// </summary>
    private unsafe void MergeScan(bool confMapped, bool colorMapped)
    {
        if (_mappedDepth is not { SizeInBytes: > 0 })
            return;

        var width = _stagingWidth;
        var height = _stagingHeight;
        if (width <= 0 || height <= 0)
            return;

        var stride = _pendingStride;
        var threshold = _pendingThreshold;
        var hasConfidence = confMapped && _mappedConf is { SizeInBytes: > 0 };
        var hasColor = colorMapped && _mappedColor is { SizeInBytes: > 0 };
        var mode = _pendingMode;
        var now = (float)_scanClock.Elapsed.TotalSeconds;
        var pointsAdded = 0;
        var skippedFull = 0;

        var mapping = _mappedDepth!.Value;
        MappedMemory mappedConf = hasConfidence ? _mappedConf.Value : default;
        MappedMemory mappedColor = hasColor ? _mappedColor.Value : default;

        // Relative depth models wobble their scale from capture to capture; on a rotation-only scan that
        // stacks repeated copies of the same surface at slightly increasing range. Pull each capture back
        // onto the points already merged before inserting it.
        var anchor = 1f;
        var shift = 0f;
        if (_pendingAutoScale && _points.Count >= MinAnchorPoints)
        {
            var estimate = EstimateAnchorScale(mapping, width, height, stride,
                                               hasConfidence ? mappedConf : (MappedMemory?)null, threshold, out shift);
            anchor = float.IsFinite(estimate) ? Math.Clamp(estimate, 0.25f, 4f) : _anchorScale;
            if (!float.IsFinite(estimate))
                shift = 0f;

            _anchorScale = anchor;
        }

        for (var y = 0; y < height; y += stride)
        {
            var row = (float*)((byte*)mapping.Data + (long)y * mapping.RowPitch);
            var belowRow = y + stride < height ? (float*)((byte*)mapping.Data + (long)(y + stride) * mapping.RowPitch) : null;
            var confRow = hasConfidence ? (float*)((byte*)mappedConf.Data + (long)y * mappedConf.RowPitch) : null;
            var colorY = hasColor ? Math.Min(_colorStagingHeight - 1, (int)((y + 0.5f) * _colorStagingHeight / height)) : 0;
            var colorRow = hasColor ? (byte*)((byte*)mappedColor.Data + (long)colorY * mappedColor.RowPitch) : null;
            for (var x = 0; x < width; x += stride)
            {
                var z = row[x];
                if (!float.IsFinite(z) || z <= 0f)
                    continue;

                if (confRow != null && confRow[x] < threshold)
                    continue;

                // Depth edges smear into flying pixels between the near and far surface
                if (IsDepthEdge(z, x + stride < width ? row[x + stride] : z, belowRow != null ? belowRow[x] : z))
                    continue;

                var r = 1f;
                var g = 1f;
                var b = 1f;
                if (colorRow != null)
                {
                    var colorX = Math.Min(_colorStagingWidth - 1, (int)((x + 0.5f) * _colorStagingWidth / width));
                    var c = colorRow + colorX * 4;
                    r = c[_pendingColorIsBgra ? 2 : 0] / 255f;
                    g = c[1] / 255f;
                    b = c[_pendingColorIsBgra ? 0 : 2] / 255f;
                }

                var depth = MathF.Max(z * _pendingScale * anchor + shift, 1e-3f);
                var position = UnprojectPoint(x, y, width, height, depth);

                // Frame centers are sharper and less distorted than the borders, so they count for more
                var ndcX = (x + 0.5f) / width * 2f - 1f;
                var ndcY = (y + 0.5f) / height * 2f - 1f;
                var weight = MathF.Max(0.05f, (1f - 0.7f * ndcX * ndcX) * (1f - 0.7f * ndcY * ndcY));
                // Raw (unanchored) samples are kept so every capture's depth scale can be solved jointly afterwards
                if (_pendingSpherical && _tiles.Count < MaxTiles && x % TileStride == 0 && y % TileStride == 0)
                {
                    var rawCell = SphericalCellIndex(UnprojectPoint(x, y, width, height, z * _pendingScale), out var rawRange);
                    if (rawCell >= 0)
                    {
                        _tileCells.Add(rawCell);
                        _tileLogRanges.Add(MathF.Log(rawRange));
                        _tileColors.Add(PackColor(r, g, b));
                        _tileWeights.Add(weight);
                    }
                }

                var inserted = _pendingSpherical ? InsertSpherical(position, r, g, b, now, weight) : InsertPoint(position, r, g, b, now);
                if (inserted == 1)
                    pointsAdded++;
                else if (inserted == 2)
                    skippedFull++;
            }
        }

        if (_tileCells.Count > 0)
        {
            _tiles.Add(new ScanTile { Cell = _tileCells.ToArray(), LogRange = _tileLogRanges.ToArray(), Color = _tileColors.ToArray(), Weight = _tileWeights.ToArray() });
            _tileCells.Clear();
            _tileLogRanges.Clear();
            _tileColors.Clear();
            _tileWeights.Clear();
        }

        _lastMergeTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        PointsAdded.Value = pointsAdded;
        _captures++;

        Captures.Value = _captures;
        RebuildOutput();
        if (_bufferFullLogged == false && skippedFull > 0)
        {
            _bufferFullLogged = true;
            SetStatus($"Point budget reached ({_points.Count} points); raise MaxPoints to keep scanning.", IStatusProvider.StatusLevel.Warning);
        }
        UpdateStatus();
    }

    /// <summary>
    /// Unprojects one depth pixel to scan space: with a camera pose, through the camera into world
    /// space, otherwise into DA3-style camera space (+Z forward, y up).
    /// </summary>
    private System.Numerics.Vector3 UnprojectPoint(float x, float y, float width, float height, float depth)
    {
        var ndcX = (x + 0.5f) / width * 2f - 1f;
        var ndcY = 1f - (y + 0.5f) / height * 2f;
        if (_pendingCamIsPose)
        {
            // RH camera space: the view direction is -Z, so flip the depth.
            var camPoint = new System.Numerics.Vector3(ndcX * _pendingTanHalfX * depth, ndcY * _pendingTanHalfY * depth, -depth);
            return System.Numerics.Vector3.Transform(camPoint, _pendingCamToWorld);
        }

        return new System.Numerics.Vector3(ndcX * _pendingTanHalfX * depth, ndcY * _pendingTanHalfY * depth, depth);
    }

    /// <summary>
    /// DA3's relative depth has no absolute scale and wobbles from capture to capture; on a rotation-only
    /// scan each capture then stacks another slightly-more-distant copy of the same surfaces. This samples
    /// the incoming capture sparsely, matches samples against the nearest already-merged points, and returns
    /// the median range ratio from the camera: the factor that pulls the capture onto the existing cloud.
    /// Returns NaN when there is not enough overlap to trust an estimate.
    /// </summary>
    private unsafe float EstimateAnchorScale(MappedMemory mapping, int width, int height, int stride,
                                             MappedMemory? conf, float threshold, out float shift)
    {
        shift = 0f;
        Span<float> sampleNew = stackalloc float[AnchorSampleCap];
        Span<float> sampleExisting = stackalloc float[AnchorSampleCap];
        var cameraOrigin = _pendingCamIsPose ? _pendingCamToWorld.Translation : default;
        Span<float> ratios = stackalloc float[AnchorSampleCap];
        var count = 0;
        var sampleStride = stride * 3;
        for (var y = 0; y < height && count < AnchorSampleCap; y += sampleStride)
        {
            var row = (float*)((byte*)mapping.Data + (long)y * mapping.RowPitch);
            var confRow = conf != null ? (float*)((byte*)conf.Value.Data + (long)y * conf.Value.RowPitch) : null;
            for (var x = 0; x < width && count < AnchorSampleCap; x += sampleStride)
            {
                var z = row[x];
                if (!float.IsFinite(z) || z <= 0f || (confRow != null && confRow[x] < threshold))
                    continue;

                var position = UnprojectPoint(x, y, width, height, z * _pendingScale);
                var found = _pendingSpherical ? TryGetSphericalPoint(position, out var nearest) : TryGetNearestPoint(position, out nearest);
                if (!found)
                    continue;

                var range = System.Numerics.Vector3.Distance(position, cameraOrigin);
                if (range < _pendingVoxelSize)
                    continue;

                var existingRange = System.Numerics.Vector3.Distance(nearest, cameraOrigin);
                sampleNew[count] = z * _pendingScale;
                sampleExisting[count] = existingRange * z * _pendingScale / range;
                ratios[count++] = Math.Clamp(existingRange / range, 0.25f, 4f);
            }
        }

        if (count < MinAnchorMatches)
            return float.NaN;

        for (var i = 1; i < count; i++) // insertion sort: tiny fixed buffer
        {
            var value = ratios[i];
            var j = i - 1;
            while (j >= 0 && ratios[j] > value)
            {
                ratios[j + 1] = ratios[j];
                j--;
            }

            ratios[j + 1] = value;
        }

        var scale = count % 2 == 1 ? ratios[count / 2] : 0.5f * (ratios[count / 2 - 1] + ratios[count / 2]);

        // Relative depth is off by a shift as well as a scale: refit existing = a * new + b on the samples that already
        // agree with the median scale, but only when the overlap spans enough depth to pin both down
        double n = 0, sx = 0, sy = 0, sxx = 0, sxy = 0;
        for (var i = 0; i < count; i++)
        {
            if (MathF.Abs(sampleExisting[i] - scale * sampleNew[i]) > 0.2f * sampleExisting[i])
                continue;

            n++;
            sx += sampleNew[i];
            sy += sampleExisting[i];
            sxx += sampleNew[i] * sampleNew[i];
            sxy += sampleNew[i] * sampleExisting[i];
        }

        if (n >= MinAnchorMatches * 2)
        {
            var meanX = sx / n;
            var meanY = sy / n;
            var variance = sxx / n - meanX * meanX;
            if (variance > 0.01 * meanX * meanX)
            {
                var slope = (sxy / n - meanX * meanY) / variance;
                var offset = meanY - slope * meanX;
                if (slope is > 0.25 and < 4.0 && Math.Abs(offset) < 0.4 * meanY)
                {
                    shift = (float)offset;
                    return (float)slope;
                }
            }
        }

        return scale;
    }

    private static bool IsDepthEdge(float z, float right, float below)
    {
        return (float.IsFinite(right) && right > 0f && MathF.Abs(right - z) > FlyingPixelRatio * z)
               || (float.IsFinite(below) && below > 0f && MathF.Abs(below - z) > FlyingPixelRatio * z);
    }

    private static long SphericalKey(System.Numerics.Vector3 position, out float range)
    {
        range = position.Length();
        if (range < 1e-5f)
            return long.MinValue;

        var elevation = MathF.Asin(Math.Clamp(position.Y / range, -1f, 1f));
        var azimuth = MathF.Atan2(position.X, -position.Z);
        var ix = (int)MathF.Floor((azimuth + MathF.PI) / SphericalCellRadians);
        var iy = (int)MathF.Floor((elevation + 0.5f * MathF.PI) / SphericalCellRadians);
        return PackVoxelKey(ix, iy, 0);
    }

    private bool TryGetSphericalPoint(System.Numerics.Vector3 position, out System.Numerics.Vector3 existing)
    {
        existing = default;
        var key = SphericalKey(position, out _);
        if (key == long.MinValue || !_voxelIndex.TryGetValue(key, out var index))
            return false;

        existing = _points[index].Position;
        return true;
    }

    /// <summary>
    /// Merges a sample into a direction-binned range map: one surface per viewing direction, so repeated and
    /// slightly mis-scaled captures of a wall average into one sheet instead of stacking copies.
    /// </summary>
    private int InsertSpherical(System.Numerics.Vector3 position, float r, float g, float b, float now, float weight)
    {
        var key = SphericalKey(position, out var range);
        if (key == long.MinValue)
            return 0;

        if (_voxelIndex.TryGetValue(key, out var index))
        {
            var sp = _points[index];
            var ratio = range / MathF.Max(sp.Position.Length(), 1e-5f);
            RecordSeam(ratio);
            if (sp.Count >= 2 && (ratio < SphericalAgreeLow || ratio > SphericalAgreeHigh))
                return 0;

            sp.Count++;
            sp.LastSeen = now;
            if (_pendingMode == MergeMode.Average)
            {
                sp.Weight += weight;
                var k = weight / sp.Weight;
                sp.Position += (position - sp.Position) * k;
                sp.Color += (new System.Numerics.Vector3(r, g, b) - sp.Color) * k;
            }
            else
            {
                sp.Position = position;
                sp.Color = new System.Numerics.Vector3(r, g, b);
            }

            _points[index] = sp;
            return 0;
        }

        if (_points.Count >= _pendingMaxPoints)
            return 2;

        _points.Add(new ScanPoint { Position = position, Color = new(r, g, b), Count = 1, Weight = weight, LastSeen = now });
        _voxelIndex[key] = _points.Count - 1;
        return 1;
    }

    /// <summary>Nearest merged point within the 3x3x3 voxel neighborhood of a position.</summary>
    private bool TryGetNearestPoint(System.Numerics.Vector3 position, out System.Numerics.Vector3 nearest)
    {
        var vs = _pendingVoxelSize;
        var vx = (int)MathF.Floor(position.X / vs);
        var vy = (int)MathF.Floor(position.Y / vs);
        var vz = (int)MathF.Floor(position.Z / vs);
        var bestDistance = float.MaxValue;
        nearest = default;
        for (var dz = -1; dz <= 1; dz++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (!_voxelIndex.TryGetValue(PackVoxelKey(vx + dx, vy + dy, vz + dz), out var index))
                        continue;

                    var distance = System.Numerics.Vector3.DistanceSquared(_points[index].Position, position);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        nearest = _points[index].Position;
                    }
                }
            }
        }

        return bestDistance != float.MaxValue;
    }

    /// <summary>
    /// Inserts one world-space sample: quantized to the voxel hash, averaged or replaced when the
    /// voxel is occupied. Returns 1 when a new voxel was created.
    /// </summary>
    private int InsertPoint(System.Numerics.Vector3 position, float r, float g, float b, float now)
    {
        var vs = _pendingVoxelSize;
        var vx = (int)MathF.Floor(position.X / vs);
        var vy = (int)MathF.Floor(position.Y / vs);
        var vz = (int)MathF.Floor(position.Z / vs);
        var key = PackVoxelKey(vx, vy, vz);
        if (_voxelIndex.TryGetValue(key, out var index))
        {
            var sp = _points[index];
            sp.Count++;
            sp.LastSeen = now;
            if (_pendingMode == MergeMode.Average)
            {
                var k = sp.Count;
                sp.Position += (position - sp.Position) / k;
                sp.Color += (new System.Numerics.Vector3(r, g, b) - sp.Color) / k;
            }
            else
            {
                sp.Position = position;
                sp.Color = new System.Numerics.Vector3(r, g, b);
            }

            _points[index] = sp;
            return 0;
        }

        if (_points.Count >= _pendingMaxPoints)
        {
            return 2;
        }

        _points.Add(new ScanPoint { Position = position, Color = new(r, g, b), Count = 1, LastSeen = now });
        _voxelIndex[key] = _points.Count - 1;
        return 1;
    }

    private static long PackVoxelKey(int vx, int vy, int vz)
    {
        long key = ((long)((vx + 0x100000) & 0x1FFFFF) << 42)
                   | ((long)((vy + 0x100000) & 0x1FFFFF) << 21)
                   | (long)((vz + 0x100000) & 0x1FFFFF);
        return key;
    }

    /// <summary>
    /// Drops points not re-observed within the lifetime, rebuilding the voxel index. Returns true
    /// when anything expired (the output was refreshed).
    /// </summary>
    private bool ExpirePoints(float lifetime)
    {
        var now = (float)_scanClock.Elapsed.TotalSeconds;
        var deadline = now - lifetime;
        var expired = 0;
        for (var i = 0; i < _points.Count; i++)
        {
            if (_points[i].LastSeen < deadline)
                expired++;
        }

        if (expired == 0)
            return false;

        var kept = new List<ScanPoint>(_points.Count - expired);
        var index = new Dictionary<long, int>();
        for (var i = 0; i < _points.Count; i++)
        {
            var sp = _points[i];
            if (sp.LastSeen < deadline)
                continue;

            index[_pendingSpherical
                      ? SphericalKey(sp.Position, out _)
                      : PackVoxelKey((int)MathF.Floor(sp.Position.X / _pendingVoxelSize),
                                     (int)MathF.Floor(sp.Position.Y / _pendingVoxelSize),
                                     (int)MathF.Floor(sp.Position.Z / _pendingVoxelSize))] = kept.Count;
            kept.Add(sp);
        }

        _points = kept;
        _voxelIndex = index;
        RebuildOutput();
        UpdateStatus();
        return true;
    }

    /// <summary>Rebuilds and uploads the drawable point buffer from the scan store.</summary>
    /** Uploads a freshly built shell to the point buffer; runs on the update thread, also after the last capture. */
    private void ApplyShellIfReady()
    {
        var shell = _shellPoints;
        if (!_shellReady || shell == null)
            return;

        _shellReady = false;
        if (_shellFlat && TryGetSeamError(out var seamMedian, out var seamP90))
            Log.Debug($"[DepthScan3D] Seam error: median {seamMedian * 100f:0.0}%, 90th percentile {seamP90 * 100f:0.0}% over {_seamSamples} overlapping samples", this);

        EnsurePointBuffer(shell, _shellCount);
        Points.Value = _pointBuffer;
        PointCount.Value = _points.Count;
    }

    /** The live cloud of a rotation-only scan is the room shell, rebuilt off-thread whenever new captures arrived. */
    private void MaybeStartShell()
    {
        var aligned = _pendingGlobalAlign && _tiles.Count >= MinTilesForAlignment;

        // Plane flattening re-detects the planes from the data so far, which would move points that were already scanned: it waits for the sweep to rest
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        var idle = now - _lastMergeTicks > FlattenIdleTicks;
        var flatNow = _pendingFlat && idle;

        // Each rebuild allocates tens of megabytes and re-creates the GPU buffer when the point count changed, which stalls the editor for a second
        // or more if it happens every few captures: while merging, rebuild at most every ShellMinIntervalTicks
        if (!idle && _shellPoints != null && now - _lastShellTicks < ShellMinIntervalTicks)
            return;

        if (_shellBusy || _shellReady || _points.Count < 3000 || (_captures == _shellCaptures && aligned == _shellAligned && RegularizeUnchanged() && _shellFlat == flatNow))
            return;

        _shellBusy = true;
        _lastShellTicks = now;
        _shellCaptures = _captures;
        _shellAligned = aligned;
        _shellRegularize = _pendingRegularize;
        _shellFloorA = _pendingFloorA;
        _shellFloorB = _pendingFloorB;
        _shellFlat = flatNow;
        var tiles = _pendingGlobalAlign && _tiles.Count >= MinTilesForAlignment ? _tiles.ToArray() : null;

        // The snapshot and the shell are reused between rebuilds: a fresh tens-of-megabytes array each time went to the large object
        // heap and its collections stalled the editor more as the cloud grew
        ScanPoint[]? snapshot = null;
        var snapshotCount = 0;
        if (tiles == null)
        {
            snapshotCount = _points.Count;
            if (_snapshotBuffer.Length < snapshotCount)
                _snapshotBuffer = new ScanPoint[snapshotCount + snapshotCount / 4 + 1024];

            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_points).CopyTo(_snapshotBuffer);
            snapshot = _snapshotBuffer;
        }

        var regularize = _pendingRegularize;
        var floorA = _pendingFloorA;
        var floorB = _pendingFloorB;
        var flat = flatNow;
        var generation = _shellGeneration;
        System.Threading.Tasks.Task.Run(() =>
                                        {
                                            try
                                            {
                                                var aligned = snapshot == null ? AlignTiles(tiles!) : null;
                                                BuildRoomShell(snapshot ?? aligned!, snapshot != null ? snapshotCount : aligned!.Length, regularize, flat, floorA, floorB, out _, out _, out _, out var positions, out var colors, out _);
                                                if (_shellBuffer.Length < positions.Count)
                                                    _shellBuffer = new Point[positions.Count + positions.Count / 4 + 1024];

                                                var shell = _shellBuffer;
                                                for (var i = 0; i < positions.Count; i++)
                                                {
                                                    shell[i] = new Point
                                                    {
                                                        Position = positions[i],
                                                        Color = new System.Numerics.Vector4(colors[i], 1f),
                                                        Orientation = System.Numerics.Quaternion.Identity,
                                                        Scale = System.Numerics.Vector3.One * (positions[i].Length() * PointScalePerRange),
                                                        F1 = 1,
                                                        F2 = 1,
                                                    };
                                                }

                                                if (generation == _shellGeneration)
                                                {
                                                    _shellPoints = shell;
                                                    _shellCount = positions.Count;
                                                    _shellReady = true;
                                                }
                                            }
                                            catch (Exception e)
                                            {
                                                Log.Warning($"[DepthScan3D] Room shell failed: {e.Message}");
                                            }
                                            finally
                                            {
                                                _shellBusy = false;
                                            }
                                        });
    }

    private void RebuildOutput()
    {
        var count = _points.Count;
        if (count == 0)
        {
            Points.Value = null;
            PointCount.Value = 0;
            return;
        }

        if (_pendingSpherical)
        {
            MaybeStartShell();
            if (_shellPoints != null)
            {
                ApplyShellIfReady();
                PointCount.Value = count;
                return;
            }
        }

        var points = new Point[count];
        var kept = 0;
        for (var i = 0; i < count; i++)
        {
            var sp = _points[i];

            // A direction seen once is as likely noise as surface
            if (_pendingSpherical && sp.Count < SphericalMinObservations)
                continue;

            points[kept++] = new Point
            {
                Position = sp.Position,
                Color = new System.Numerics.Vector4(sp.Color, 1f),
                Orientation = System.Numerics.Quaternion.Identity,
                Scale = _pendingSpherical ? System.Numerics.Vector3.One * (sp.Position.Length() * PointScalePerRange * 0.5f) : System.Numerics.Vector3.One,
                F1 = 1,
                F2 = 1,
            };
        }

        count = kept;
        if (count == 0)
        {
            Points.Value = null;
            PointCount.Value = 0;
            return;
        }

        if (kept < points.Length)
            Array.Resize(ref points, kept);

        EnsurePointBuffer(points, count);
        Points.Value = _pointBuffer;
        PointCount.Value = count;
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
            using (var data = new DataStream(bytesNeeded, true, true))
            {
                data.WriteRange(points, 0, count);
                data.Position = 0;
                ResourceManager.SetupStructuredBuffer(data, bytesNeeded, Point.Stride, ref _pointBuffer.Buffer);
            }

            ResourceManager.CreateStructuredBufferSrv(_pointBuffer.Buffer, ref _pointBuffer.Srv);
            ResourceManager.CreateStructuredBufferUav(_pointBuffer.Buffer, UnorderedAccessViewBufferFlags.None, ref _pointBuffer.Uav);
            return;
        }

        ResourceManager.Device.ImmediateContext.UpdateSubresource(_pointBuffer.Buffer!, 0, System.Runtime.InteropServices.MemoryMarshal.AsBytes(points.AsSpan(0, count)), 0, 0);
    }

    private void ResetScan()
    {
        _tiles.Clear();
        _tileCells.Clear();
        _tileLogRanges.Clear();
        _tileColors.Clear();
        _tileWeights.Clear();
        _shellGeneration++;
        _shellPoints = null;
        _shellReady = false;
        _shellCaptures = 0;
        _points.Clear();
        _voxelIndex.Clear();
        _captures = 0;
        _pointsAdded = 0;
        _bufferFullLogged = false;
        _anchorScale = 1f;
        Points.Value = null;
        PointCount.Value = 0;
        Captures.Value = 0;
        PointsAdded.Value = 0;
        SavedPath.Value = string.Empty;
        Array.Clear(_seamHistogram);
        _seamSamples = 0;
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
        _colorPending = false;
        _stagingWidth = width;
        _stagingHeight = height;
        _depthStaging?.Dispose();
        _confStaging?.Dispose();
        _colorStaging?.Dispose();
        _depthStaging = CreateStagingTexture(width, height, Format.R32_Float);
        _confStaging = new T3.Graphics.Compat.Texture2D(ResourceManager.Device, new Texture2DDescription
        {
            Width = width, Height = height, MipLevels = 1, ArraySize = 1,
            Format = Format.R32_Float, SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging, BindFlags = BindFlags.None,
            CpuAccessFlags = CpuAccessFlags.Read, OptionFlags = ResourceOptionFlags.None
        });
        _colorStaging = null;
    }

    /** The color map keeps its own size and format, so its staging copy is sized independently of the depth. */
    private void GetOrCreateColorStaging(int width, int height, Format format)
    {
        if (_colorStaging is { IsDisposed: false }
            && _colorStaging.Description.Width == width
            && _colorStaging.Description.Height == height
            && _colorStaging.Description.Format == format)
            return;

        _colorPending = false;
        _colorStaging?.Dispose();
        _colorStagingWidth = width;
        _colorStagingHeight = height;
        _colorStaging = new T3.Graphics.Compat.Texture2D(ResourceManager.Device, new Texture2DDescription
        {
            Width = width, Height = height, MipLevels = 1, ArraySize = 1,
            Format = format, SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging, BindFlags = BindFlags.None,
            CpuAccessFlags = CpuAccessFlags.Read, OptionFlags = ResourceOptionFlags.None
        });
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
    private void UnmapStaging()
    {
        var context = ResourceManager.Device.ImmediateContext;
        if (_mappedDepth != null && _depthStaging is { IsDisposed: false })
            context.UnmapSubresource(_depthStaging, 0);
        if (_mappedConf != null && _confStaging is { IsDisposed: false })
            context.UnmapSubresource(_confStaging, 0);
        if (_mappedColor != null && _colorStaging is { IsDisposed: false })
            context.UnmapSubresource(_colorStaging, 0);

        _mappedDepth = null;
        _mappedConf = null;
        _mappedColor = null;
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
        _skippedLost = 0;
        ResetScan();
        SetStatus("The scan was cleared.", IStatusProvider.StatusLevel.Notice);
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

    private void HandleSave(EvaluationContext context)
    {
        if (!MathUtils.WasTriggered(SaveTrigger.GetValue(context), ref _saveState))
            return;

        SaveTrigger.SetTypedInputValue(false);
        _saveState = false;

        if (_points.Count == 0)
        {
            SetStatus("No scan points to save; capture first.", IStatusProvider.StatusLevel.Warning);
            SavedPath.Value = string.Empty;
            return;
        }

        var format = (PlyFormat)Math.Clamp(SaveFormatInput.GetValue(context), 0, 2);
        var relativePath = BuildRelativePath(SaveFolder.GetValue(context) ?? string.Empty,
                                             SaveFileName.GetValue(context) ?? string.Empty,
                                             ".ply");

        if (relativePath.Length == 0)
        {
            SaveStatus(string.Empty, "The file name is empty; the scan was not saved.");
            return;
        }

        if (!AssetRegistry.TryResolveAddressForWriting(relativePath, this, out var absolutePath, out var failureReason))
        {
            SaveStatus(string.Empty, $"The scan could not be written: {failureReason}");
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            if (format == PlyFormat.Mesh)
            {
                if (!_pendingSpherical)
                {
                    SaveStatus(string.Empty, "Mesh export needs a camera that rotates about the origin (a PTZ scan).");
                    return;
                }

                if (_meshBusy)
                {
                    SetStatus("A mesh is still being built.", IStatusProvider.StatusLevel.Warning);
                    return;
                }

                // The meshing walks millions of cells: it runs on a snapshot off the editor thread
                var meshTiles = _pendingGlobalAlign && _tiles.Count >= MinTilesForAlignment ? _tiles.ToArray() : null;
                var snapshot = meshTiles == null ? _points.ToArray() : null;
                var regularize = _pendingRegularize;
                var flat = _pendingFlat;
                var floorA = _pendingFloorA;
                var floorB = _pendingFloorB;
                _meshBusy = true;
                SetStatus($"Building a mesh from {snapshot?.Length ?? meshTiles!.Length} {(snapshot != null ? "points" : "captures")}...", IStatusProvider.StatusLevel.Notice);
                System.Threading.Tasks.Task.Run(() =>
                                                {
                                                    try
                                                    {
                                                        var watch = System.Diagnostics.Stopwatch.StartNew();
                                                        var (vertices, triangles) = WriteSphericalMesh(absolutePath, snapshot ?? AlignTiles(meshTiles!), regularize, flat, floorA, floorB);
                                                        _meshResultPath = relativePath;
                                                        _meshResultMessage = $"Saved a mesh of {vertices} vertices and {triangles} triangles to {relativePath} ({watch.Elapsed.TotalSeconds:F1} s)";
                                                    }
                                                    catch (Exception e)
                                                    {
                                                        _meshResultPath = string.Empty;
                                                        _meshResultMessage = $"Building the mesh failed: {e.Message}";
                                                    }
                                                    finally
                                                    {
                                                        _meshBusy = false;
                                                        _meshDone = true;
                                                    }
                                                });
                return;
            }
            else
            {
                WritePly(absolutePath, format == PlyFormat.Ascii);
                SavedPath.Value = relativePath;
                SetStatus($"Saved {_points.Count} points ({_captures} captures) to {relativePath}", IStatusProvider.StatusLevel.Success);
            }
        }
        catch (Exception e)
        {
            SaveStatus(string.Empty, $"Saving the scan failed: {e.Message}");
            Log.Error($"[DepthScan3D] Saving {absolutePath} failed: {e.Message}", this);
        }
    }

    private void SaveStatus(string path, string message)
    {
        SavedPath.Value = path;
        SetStatus(message, IStatusProvider.StatusLevel.Error);
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
    /// Writes the scan as a PLY point cloud (ASCII or binary little-endian): float32 x/y/z plus
    /// uchar red/green/blue per vertex, openable in MeshLab/CloudCompare/blender.
    /// </summary>
    /** Bins the scan into a range map, fits the room box and returns the box-snapped shell as a half-degree vertex grid. */
    private static void BuildRoomShell(ScanPoint[] scanPoints, int scanCount, bool regularize, bool flat, float floorA, float floorB, out int w2, out int h2, out float[] gridRange,
                                       out List<System.Numerics.Vector3> positions, out List<System.Numerics.Vector3> colors,
                                       out int[] vertexIndex)
    {
        var width = (int)MathF.Ceiling(2f * MathF.PI / SphericalCellRadians);
        var height = (int)MathF.Ceiling(MathF.PI / SphericalCellRadians) + 1;
        var range = new float[width * height];
        var color = new System.Numerics.Vector3[width * height];
        Array.Fill(range, float.NaN);
        for (var scanIndex = 0; scanIndex < scanCount; scanIndex++)
        {
            var sp = scanPoints[scanIndex];
            if (sp.Count < SphericalMinObservations)
                continue;

            var r = sp.Position.Length();
            var e = MathF.Asin(Math.Clamp(sp.Position.Y / r, -1f, 1f));
            var a = MathF.Atan2(sp.Position.X, -sp.Position.Z);
            var ix = Math.Clamp((int)MathF.Floor((a + MathF.PI) / SphericalCellRadians), 0, width - 1);
            var iy = Math.Clamp((int)MathF.Floor((0.5f * MathF.PI - e) / SphericalCellRadians), 0, height - 1);
            range[iy * width + ix] = r;
            color[iy * width + ix] = sp.Color;
        }

        MedianFilterRange(range, width, height);
        FillSmallHoles(range, color, width, height, 6);

        // The depth of each capture carries its own scale, so surfaces wobble. A room is a box: fit one and snap the walls to it
        var box = regularize ? FitRoomBox(range, width, height) : default;

        // Output grid at half the resolution; walls are flat, so the extra detail only matters for objects
        const int downsample = 2;
        w2 = width / downsample;
        h2 = height / downsample;
        vertexIndex = new int[w2 * h2];
        gridRange = new float[w2 * h2];
        positions = new List<System.Numerics.Vector3>();
        colors = new List<System.Numerics.Vector3>();
        // First pass: the observed range and colour of every output cell
        var observedRange = new float[w2 * h2];
        var observedColor = new System.Numerics.Vector3[w2 * h2];
        for (var y = 0; y < h2; y++)
        {
            for (var x = 0; x < w2; x++)
            {
                var sum = 0f;
                var colorSum = System.Numerics.Vector3.Zero;
                var n = 0;
                for (var by = 0; by < downsample; by++)
                {
                    for (var bx = 0; bx < downsample; bx++)
                    {
                        var index = (y * downsample + by) * width + x * downsample + bx;
                        if (float.IsNaN(range[index]))
                            continue;

                        sum += range[index];
                        colorSum += color[index];
                        n++;
                    }
                }

                observedRange[y * w2 + x] = n > 0 ? sum / n : float.NaN;
                observedColor[y * w2 + x] = n > 0 ? colorSum / n : default;
            }
        }

        if (flat && !regularize)
            FlattenPlanes(observedRange, w2, h2);

        for (var y = 0; y < h2; y++)
        {
            var elevation = 0.5f * MathF.PI - (y + 0.5f) * SphericalCellRadians * downsample;
            var cosE = MathF.Cos(elevation);
            var sinE = MathF.Sin(elevation);
            for (var x = 0; x < w2; x++)
            {
                var azimuth = (x + 0.5f) * SphericalCellRadians * downsample - MathF.PI;
                var dx = cosE * MathF.Sin(azimuth);
                var dz = -cosE * MathF.Cos(azimuth);
                var boxRange = regularize ? box.RangeAlong(dx, sinE, dz) : float.MaxValue;
                var observed = observedRange[y * w2 + x];

                // Without a box only what was seen becomes geometry; with one, walls and anything not clearly in
                // front of them take the box, and unseen directions are filled from it
                var r = boxRange;
                var cellColor = new System.Numerics.Vector3(0.4f, 0.4f, 0.4f);
                if (!float.IsNaN(observed))
                {
                    cellColor = observedColor[y * w2 + x];
                    if (!regularize || observed < boxRange * RoomObjectRatio)
                        r = observed;
                }
                else if (!regularize)
                {
                    gridRange[y * w2 + x] = float.NaN;
                    vertexIndex[y * w2 + x] = -1;
                    continue;
                }

                gridRange[y * w2 + x] = r;
                vertexIndex[y * w2 + x] = positions.Count;
                positions.Add(new System.Numerics.Vector3(r * dx, r * sinE, r * dz));
                colors.Add(cellColor);
            }
        }

        if (floorA > 0f && floorB > 0f && !regularize)
            StretchFloorPlan(positions, floorA, floorB);
    }

    /**
     * Stretches the floor plan to a known room size. The depth model's scale differs along the room's two axes, and with one rotating camera
     * there is no parallax to tell, so the measured wall-to-wall spans (trimmed, on the band just above the horizon) are mapped to the given
     * dimensions along the best-fit axes. Heights are untouched, so floors, ceilings and vertical walls stay level and vertical.
     */
    private static void StretchFloorPlan(List<System.Numerics.Vector3> positions, float sizeA, float sizeB)
    {
        var band = new List<System.Numerics.Vector2>();
        foreach (var p in positions)
        {
            var length = p.Length();
            if (length > 1e-4f && p.Y / length is > 0f and < 0.5f)
                band.Add(new System.Numerics.Vector2(p.X, p.Z));
        }

        if (band.Count < 500)
            return;

        var step = Math.Max(1, band.Count / 20000);
        var sample = new List<System.Numerics.Vector2>();
        for (var i = 0; i < band.Count; i += step)
            sample.Add(band[i]);

        var u = new float[sample.Count];
        var v = new float[sample.Count];
        var bestArea = float.MaxValue;
        var bestAngle = 0f;
        var bestSpanU = 0f;
        var bestSpanV = 0f;
        for (var degrees = 0; degrees < 90; degrees++)
        {
            var angle = degrees * MathF.PI / 180f;
            var c = MathF.Cos(angle);
            var s = MathF.Sin(angle);
            for (var i = 0; i < sample.Count; i++)
            {
                u[i] = sample[i].X * c - sample[i].Y * s;
                v[i] = sample[i].X * s + sample[i].Y * c;
            }

            Array.Sort(u);
            Array.Sort(v);
            var lowIndex = (int)(sample.Count * 0.03f);
            var highIndex = (int)(sample.Count * 0.97f);
            var spanU = u[highIndex] - u[lowIndex];
            var spanV = v[highIndex] - v[lowIndex];
            if (spanU * spanV < bestArea)
            {
                bestArea = spanU * spanV;
                bestAngle = angle;
                bestSpanU = spanU;
                bestSpanV = spanV;
            }
        }

        if (bestSpanU < 0.5f || bestSpanV < 0.5f)
            return;

        var large = Math.Max(sizeA, sizeB);
        var small = Math.Min(sizeA, sizeB);
        var scaleU = bestSpanU >= bestSpanV ? large / bestSpanU : small / bestSpanU;
        var scaleV = bestSpanU >= bestSpanV ? small / bestSpanV : large / bestSpanV;
        var cosA = MathF.Cos(bestAngle);
        var sinA = MathF.Sin(bestAngle);
        for (var i = 0; i < positions.Count; i++)
        {
            var p = positions[i];
            var pu = (p.X * cosA - p.Z * sinA) * scaleU;
            var pv = (p.X * sinA + p.Z * cosA) * scaleV;
            positions[i] = new System.Numerics.Vector3(pu * cosA + pv * sinA, p.Y, -pu * sinA + pv * cosA);
        }
    }

    private static readonly int GridWidth = (int)MathF.Ceiling(2f * MathF.PI / SphericalCellRadians);
    private static readonly int GridHeight = (int)MathF.Ceiling(MathF.PI / SphericalCellRadians) + 1;

    /** Alignment works on 0.5 degree cells, the resolution of the shell, so a few samples per capture reach every cell. */
    private static readonly int CoarseWidth = GridWidth / 2;
    private static readonly int CoarseHeight = (GridHeight + 1) / 2;

    private static uint PackColor(float r, float g, float b) =>
        ((uint)Math.Clamp((int)(r * 255f + 0.5f), 0, 255) << 16) | ((uint)Math.Clamp((int)(g * 255f + 0.5f), 0, 255) << 8) | (uint)Math.Clamp((int)(b * 255f + 0.5f), 0, 255);

    /** Index of the 0.5 degree direction cell of a position seen from the origin; -1 when the position is the origin. */
    private static int SphericalCellIndex(System.Numerics.Vector3 position, out float range)
    {
        range = position.Length();
        if (range < 1e-5f)
            return -1;

        var elevation = MathF.Asin(Math.Clamp(position.Y / range, -1f, 1f));
        var azimuth = MathF.Atan2(position.X, -position.Z);
        var cell = 2f * SphericalCellRadians;
        var ix = Math.Clamp((int)MathF.Floor((azimuth + MathF.PI) / cell), 0, CoarseWidth - 1);
        var iy = Math.Clamp((int)MathF.Floor((0.5f * MathF.PI - elevation) / cell), 0, CoarseHeight - 1);
        return iy * CoarseWidth + ix;
    }

    /**
     * Solves one depth scale per capture from everywhere the captures overlap: every direction cell seen by several captures
     * must have one range, so log s_tile + log range is pulled to the cell's consensus (alternating least squares with
     * Huber weights, a weak prior toward the captured scale). Returns the map with every sample rescaled and averaged.
     * Relative depth has no scale of its own, so the sequential per-capture anchor alone chains errors; this uses all overlaps at once.
     */
    private static ScanPoint[] AlignTiles(ScanTile[] tiles)
    {
        const int iterations = 25;
        const float huber = 0.08f;
        const float prior = 4f;

        var total = 0;
        foreach (var tile in tiles)
            total += tile.Cell.Length;

        // Group samples by cell
        var cells = new int[total];
        var tileOf = new int[total];
        var logRange = new float[total];
        var weight = new float[total];
        var colorOf = new uint[total];
        var n = 0;
        for (var t = 0; t < tiles.Length; t++)
        {
            var tile = tiles[t];
            for (var i = 0; i < tile.Cell.Length; i++, n++)
            {
                cells[n] = tile.Cell[i];
                tileOf[n] = t;
                logRange[n] = tile.LogRange[i];
                weight[n] = tile.Weight[i];
                colorOf[n] = tile.Color[i];
            }
        }

        var order = new int[total];
        for (var i = 0; i < total; i++)
            order[i] = i;

        Array.Sort(cells.AsSpan().ToArray(), order);
        var sortedCells = new int[total];
        for (var i = 0; i < total; i++)
            sortedCells[i] = cells[order[i]];

        // Cell group boundaries; only cells seen by more than one sample constrain the scales
        var groupStart = new List<int>();
        for (var i = 0; i < total; i++)
        {
            if (i == 0 || sortedCells[i] != sortedCells[i - 1])
                groupStart.Add(i);
        }

        groupStart.Add(total);
        var groups = groupStart.Count - 1;

        var x = new float[tiles.Length];
        var robust = new float[total];
        for (var i = 0; i < total; i++)
            robust[i] = 1f;

        var tileNumerator = new double[tiles.Length];
        var tileDenominator = new double[tiles.Length];
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            Array.Clear(tileNumerator);
            Array.Clear(tileDenominator);
            for (var g = 0; g < groups; g++)
            {
                var a = groupStart[g];
                var b = groupStart[g + 1];
                if (b - a < 2)
                    continue;

                double sum = 0, sumW = 0;
                for (var k = a; k < b; k++)
                {
                    var o = order[k];
                    var w = weight[o] * robust[o];
                    sum += w * (x[tileOf[o]] + logRange[o]);
                    sumW += w;
                }

                if (sumW < 1e-9)
                    continue;

                var consensus = sum / sumW;
                for (var k = a; k < b; k++)
                {
                    var o = order[k];
                    var w = weight[o] * robust[o];
                    tileNumerator[tileOf[o]] += w * (consensus - logRange[o]);
                    tileDenominator[tileOf[o]] += w;
                    var residual = MathF.Abs((float)(x[tileOf[o]] + logRange[o] - consensus));
                    robust[o] = residual <= huber ? 1f : huber / residual;
                }
            }

            for (var t = 0; t < tiles.Length; t++)
                x[t] = (float)(tileNumerator[t] / (tileDenominator[t] + prior));
        }

        // The aligned map: per cell the robust mean of the rescaled samples
        var points = new List<ScanPoint>();
        for (var g = 0; g < groups; g++)
        {
            var a = groupStart[g];
            var b = groupStart[g + 1];
            double sum = 0, sumW = 0, cr = 0, cg = 0, cb = 0;
            for (var k = a; k < b; k++)
            {
                var o = order[k];
                var w = weight[o] * robust[o];
                sum += w * (x[tileOf[o]] + logRange[o]);
                sumW += w;
                cr += w * ((colorOf[o] >> 16) & 255);
                cg += w * ((colorOf[o] >> 8) & 255);
                cb += w * (colorOf[o] & 255);
            }

            if (sumW < 1e-9)
                continue;

            var coarse = sortedCells[a];
            var range = (float)Math.Exp(sum / sumW);
            var color = new System.Numerics.Vector3((float)(cr / sumW) / 255f, (float)(cg / sumW) / 255f, (float)(cb / sumW) / 255f);
            for (var sy = 0; sy < 2; sy++)
            {
                for (var sx = 0; sx < 2; sx++)
                {
                    var fineX = (coarse % CoarseWidth) * 2 + sx;
                    var fineY = Math.Min((coarse / CoarseWidth) * 2 + sy, GridHeight - 1);
                    var elevation = 0.5f * MathF.PI - (fineY + 0.5f) * SphericalCellRadians;
                    var azimuth = (fineX + 0.5f) * SphericalCellRadians - MathF.PI;
                    var cosE = MathF.Cos(elevation);
                    points.Add(new ScanPoint
                    {
                        Position = new System.Numerics.Vector3(range * cosE * MathF.Sin(azimuth), range * MathF.Sin(elevation), -range * cosE * MathF.Cos(azimuth)),
                        Color = color,
                        Count = b - a,
                        Weight = (float)sumW,
                    });
                }
            }
        }

        return points.ToArray();
    }

    /**
     * Turns the spherical range map into a closed triangle mesh: sparse cells are dropped, the range grid is median
     * filtered and small holes are filled, neighbouring cells become triangles unless a depth jump separates them
     * (so surfaces never stretch across an edge), and the open rim at the lowest seen elevation is closed with a
     * floor fan so the shell is solid. Faces point toward the camera.
     */
    private (int Vertices, int Triangles) WriteSphericalMesh(string absolutePath, ScanPoint[] scanPoints, bool regularize, bool flat, float floorA, float floorB)
    {
        BuildRoomShell(scanPoints, scanPoints.Length, regularize, flat, floorA, floorB, out var w2, out var h2, out var gridRange, out var positions, out var colors, out var vertexIndex);

        var faces = new List<int>();
        void AddFace(int i0, int i1, int i2)
        {
            var normal = System.Numerics.Vector3.Cross(positions[i1] - positions[i0], positions[i2] - positions[i0]);
            var centroid = positions[i0] + positions[i1] + positions[i2];
            if (System.Numerics.Vector3.Dot(normal, centroid) > 0f)
                (i1, i2) = (i2, i1);

            faces.Add(i0);
            faces.Add(i1);
            faces.Add(i2);
        }

        for (var y = 0; y < h2 - 1; y++)
        {
            for (var x = 0; x < w2; x++)
            {
                var x1 = (x + 1) % w2;
                var i00 = vertexIndex[y * w2 + x];
                var i10 = vertexIndex[y * w2 + x1];
                var i01 = vertexIndex[(y + 1) * w2 + x];
                var i11 = vertexIndex[(y + 1) * w2 + x1];
                var r00 = gridRange[y * w2 + x];
                var r10 = gridRange[y * w2 + x1];
                var r01 = gridRange[(y + 1) * w2 + x];
                var r11 = gridRange[(y + 1) * w2 + x1];
                if (i00 < 0 || i10 < 0 || i01 < 0 || i11 < 0)
                    continue;

                var lo = MathF.Min(MathF.Min(r00, r10), MathF.Min(r01, r11));
                var hi = MathF.Max(MathF.Max(r00, r10), MathF.Max(r01, r11));
                if (hi > lo * (regularize ? MeshDepthJumpRatio : MeshScanJumpRatio))
                    continue;

                AddFace(i00, i10, i01);
                AddFace(i10, i11, i01);
            }
        }

        CloseHoles(positions, colors, faces, AddFace, regularize);

        using (var file = File.Create(absolutePath))
        {
            var header = "ply\nformat binary_little_endian 1.0\ncomment TiXL DepthScan3D mesh\n"
                         + $"element vertex {positions.Count}\nproperty float x\nproperty float y\nproperty float z\n"
                         + "property uchar red\nproperty uchar green\nproperty uchar blue\n"
                         + $"element face {faces.Count / 3}\nproperty list uchar int vertex_indices\nend_header\n";
            var headerBytes = System.Text.Encoding.ASCII.GetBytes(header);
            file.Write(headerBytes, 0, headerBytes.Length);
            using var writer = new BinaryWriter(file);
            for (var i = 0; i < positions.Count; i++)
            {
                writer.Write(positions[i].X);
                writer.Write(positions[i].Y);
                writer.Write(positions[i].Z);
                writer.Write(ToByte(colors[i].X));
                writer.Write(ToByte(colors[i].Y));
                writer.Write(ToByte(colors[i].Z));
            }

            for (var i = 0; i < faces.Count; i += 3)
            {
                writer.Write((byte)3);
                writer.Write(faces[i]);
                writer.Write(faces[i + 1]);
                writer.Write(faces[i + 2]);
            }
        }

        return (positions.Count, faces.Count / 3);
    }

    /**
     * Fans over every boundary loop of the mesh so the shell is solid: gaps left by depth jumps and missing cells close on
     * their centroid, and the large outer rim (the lowest elevation the camera could see) closes on a point at floor level.
     */
    private static void CloseHoles(List<System.Numerics.Vector3> positions, List<System.Numerics.Vector3> colors, List<int> faces,
                                   Action<int, int, int> addFace, bool closeLargeLoops)
    {
        // Tuple keys: a packed 64-bit key hashes to a ^ b, which piles neighbouring edges into a few buckets
        var directed = new HashSet<(int, int)>();
        for (var i = 0; i < faces.Count; i += 3)
        {
            for (var k = 0; k < 3; k++)
                directed.Add((faces[i + k], faces[i + (k + 1) % 3]));
        }

        // Boundary edges, reversed so that walking them follows the open side
        var outgoing = new Dictionary<int, List<int>>();
        foreach (var (from, to) in directed)
        {
            if (directed.Contains((to, from)))
                continue;

            if (!outgoing.TryGetValue(to, out var list))
                outgoing[to] = list = new List<int>(2);

            list.Add(from);
        }

        var loop = new List<int>();
        foreach (var start in new List<int>(outgoing.Keys))
        {
            while (outgoing[start].Count > 0)
            {
                loop.Clear();
                var current = start;
                var closed = false;
                while (true)
                {
                    loop.Add(current);
                    if (!outgoing.TryGetValue(current, out var options) || options.Count == 0)
                        break;

                    var following = options[^1];
                    options.RemoveAt(options.Count - 1);
                    current = following;
                    if (current == start)
                    {
                        closed = true;
                        break;
                    }
                }

                if (!closed || loop.Count < 3 || (!closeLargeLoops && loop.Count > MeshSmallHoleEdges))
                    continue;

                var center = System.Numerics.Vector3.Zero;
                var color = System.Numerics.Vector3.Zero;
                var lowest = float.MaxValue;
                foreach (var vertex in loop)
                {
                    center += positions[vertex];
                    color += colors[vertex];
                    lowest = MathF.Min(lowest, positions[vertex].Y);
                }

                center /= loop.Count;
                if (loop.Count > MeshRimLoopEdges)
                {
                    center.X = 0f;
                    center.Z = 0f;
                    center.Y = lowest;
                    color = new System.Numerics.Vector3(0.5f, 0.5f, 0.5f) * loop.Count;
                }

                var centerIndex = positions.Count;
                positions.Add(center);
                colors.Add(color / loop.Count);
                for (var i = 0; i < loop.Count; i++)
                    addFace(loop[i], loop[(i + 1) % loop.Count], centerIndex);
            }
        }
    }

    /**
     * Flattens the surfaces of a rotation-only scan onto planes found in the scan itself: no box and no right angles, so tilted
     * ceilings, alcoves and desks work as well as walls. Surface normals come from the range grid; planes are found by
     * RANSAC on (position, normal) pairs, largest first. Cells whose normal agrees with a plane and that lie within a generous
     * band of it (relative depth makes one wall appear at several distances) are moved onto it along their viewing ray.
     * Cells that fit no plane (clutter, edges) keep their range.
     */
    private static void FlattenPlanes(float[] range, int w, int h)
    {
        const float depthJump = 0.2f;
        const float normalCos = 0.8f;          // ~37 degrees
        const float ransacBand = 0.08f;        // fraction of range
        const float assignBand = 0.25f;
        const int candidates = 300;
        const int sampleSize = 5000;
        const int maxPlanes = 16;

        var count = w * h;
        var positions = new System.Numerics.Vector3[count];
        var directions = new System.Numerics.Vector3[count];
        for (var y = 0; y < h; y++)
        {
            var elevation = 0.5f * MathF.PI - (y + 0.5f) * 2f * SphericalCellRadians;
            var cosE = MathF.Cos(elevation);
            var sinE = MathF.Sin(elevation);
            for (var x = 0; x < w; x++)
            {
                var azimuth = (x + 0.5f) * 2f * SphericalCellRadians - MathF.PI;
                var d = new System.Numerics.Vector3(cosE * MathF.Sin(azimuth), sinE, -cosE * MathF.Cos(azimuth));
                directions[y * w + x] = d;
                if (!float.IsNaN(range[y * w + x]))
                    positions[y * w + x] = d * range[y * w + x];
            }
        }

        // Normals from central differences, facing the camera; edges (depth jumps) get none
        var normals = new System.Numerics.Vector3[count];
        var hasNormal = new bool[count];
        var valid = new List<int>();
        for (var y = 1; y < h - 1; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var c = y * w + x;
                var left = y * w + (x + w - 1) % w;
                var right = y * w + (x + 1) % w;
                var up = (y - 1) * w + x;
                var down = (y + 1) * w + x;
                var rc = range[c];
                if (float.IsNaN(rc) || float.IsNaN(range[left]) || float.IsNaN(range[right]) || float.IsNaN(range[up]) || float.IsNaN(range[down]))
                    continue;

                if (MathF.Abs(range[left] - rc) > depthJump * rc || MathF.Abs(range[right] - rc) > depthJump * rc
                    || MathF.Abs(range[up] - rc) > depthJump * rc || MathF.Abs(range[down] - rc) > depthJump * rc)
                    continue;

                var n = System.Numerics.Vector3.Cross(positions[right] - positions[left], positions[up] - positions[down]);
                var length = n.Length();
                if (length < 1e-9f)
                    continue;

                n /= length;
                if (System.Numerics.Vector3.Dot(n, positions[c]) > 0f)
                    n = -n;

                normals[c] = n;
                hasNormal[c] = true;
                valid.Add(c);
            }
        }

        if (valid.Count < 500)
            return;

        var random = new Random(11);
        var remaining = new List<int>(valid);
        var planeNormal = new List<System.Numerics.Vector3>();
        var planeOffset = new List<float>();
        var minSupport = Math.Max(150, valid.Count / 80);
        for (var plane = 0; plane < maxPlanes && remaining.Count >= minSupport; plane++)
        {
            var sample = new int[Math.Min(sampleSize, remaining.Count)];
            for (var i = 0; i < sample.Length; i++)
                sample[i] = remaining[random.Next(remaining.Count)];

            var bestScore = 0;
            var bestNormal = System.Numerics.Vector3.Zero;
            var bestOffset = 0f;
            for (var k = 0; k < candidates; k++)
            {
                var seed = remaining[random.Next(remaining.Count)];
                var n = normals[seed];
                var d = -System.Numerics.Vector3.Dot(n, positions[seed]);
                var score = 0;
                for (var i = 0; i < sample.Length; i++)
                {
                    var j = sample[i];
                    if (System.Numerics.Vector3.Dot(normals[j], n) < normalCos)
                        continue;

                    if (MathF.Abs(System.Numerics.Vector3.Dot(n, positions[j]) + d) < ransacBand * range[j])
                        score++;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestNormal = n;
                    bestOffset = d;
                }
            }

            if (bestScore * remaining.Count / sample.Length < minSupport)
                break;

            // Refine on every remaining cell that agrees, then drop them from the pool
            var sumNormal = System.Numerics.Vector3.Zero;
            var inliers = new List<int>();
            foreach (var j in remaining)
            {
                if (System.Numerics.Vector3.Dot(normals[j], bestNormal) >= normalCos
                    && MathF.Abs(System.Numerics.Vector3.Dot(bestNormal, positions[j]) + bestOffset) < 2f * ransacBand * range[j])
                {
                    inliers.Add(j);
                    sumNormal += normals[j];
                }
            }

            if (inliers.Count < minSupport || sumNormal.LengthSquared() < 1e-9f)
                break;

            var refined = System.Numerics.Vector3.Normalize(sumNormal);
            var offset = 0f;
            foreach (var j in inliers)
                offset -= System.Numerics.Vector3.Dot(refined, positions[j]);

            offset /= inliers.Count;
            planeNormal.Add(refined);
            planeOffset.Add(offset);
            var inlierSet = new HashSet<int>(inliers);
            remaining.RemoveAll(inlierSet.Contains);
        }

        // Every cell with a normal takes the largest plane it agrees with, within a generous band, along its own viewing ray
        var flattened = (float[])range.Clone();
        foreach (var c in valid)
        {
            for (var p = 0; p < planeNormal.Count; p++)
            {
                var n = planeNormal[p];
                if (System.Numerics.Vector3.Dot(normals[c], n) < normalCos)
                    continue;

                var facing = System.Numerics.Vector3.Dot(n, directions[c]);
                if (facing > -0.15f)
                    continue;

                var onPlane = -planeOffset[p] / facing;
                if (onPlane > range[c] * (1f - assignBand) && onPlane < range[c] * (1f + assignBand))
                {
                    flattened[c] = onPlane;
                    break;
                }
            }
        }

        Array.Copy(flattened, range, count);
    }

    /** A room as a box rotated about the vertical axis; the camera sits inside it at the origin. */
    private readonly struct RoomBox
    {
        public RoomBox(float yaw, float xp, float xn, float zp, float zn, float yp, float yn)
        {
            Yaw = yaw;
            XP = xp;
            XN = xn;
            ZP = zp;
            ZN = zn;
            YP = yp;
            YN = yn;
            Cos = MathF.Cos(-yaw);
            Sin = MathF.Sin(-yaw);
        }

        public readonly float Yaw, XP, XN, ZP, ZN, YP, YN, Cos, Sin;

        public float RangeAlong(float dx, float dy, float dz)
        {
            var x = Cos * dx + Sin * dz;
            var z = -Sin * dx + Cos * dz;
            var best = float.MaxValue;
            if (x > 1e-6f) best = MathF.Min(best, XP / x); else if (x < -1e-6f) best = MathF.Min(best, XN / -x);
            if (z > 1e-6f) best = MathF.Min(best, ZP / z); else if (z < -1e-6f) best = MathF.Min(best, ZN / -z);
            if (dy > 1e-6f) best = MathF.Min(best, YP / dy); else if (dy < -1e-6f) best = MathF.Min(best, YN / -dy);
            return best;
        }
    }

    /**
     * Robust fit of a room box to the range map: a truncated log-range error, so furniture and the depth wobble between
     * captures cannot pull the walls. Pattern search from several yaw and size starts.
     */
    private static RoomBox FitRoomBox(float[] range, int width, int height)
    {
        const int maxSamples = 6000;
        var valid = 0;
        for (var i = 0; i < range.Length; i++)
        {
            if (!float.IsNaN(range[i]))
                valid++;
        }

        var step = Math.Max(1, valid / maxSamples);
        var dirX = new List<float>();
        var dirY = new List<float>();
        var dirZ = new List<float>();
        var logRange = new List<float>();
        var seen = 0;
        var rangeSum = 0f;
        for (var y = 0; y < height; y++)
        {
            var elevation = 0.5f * MathF.PI - (y + 0.5f) * SphericalCellRadians;
            for (var x = 0; x < width; x++)
            {
                var r = range[y * width + x];
                if (float.IsNaN(r) || seen++ % step != 0)
                    continue;

                var azimuth = (x + 0.5f) * SphericalCellRadians - MathF.PI;
                dirX.Add(MathF.Cos(elevation) * MathF.Sin(azimuth));
                dirY.Add(MathF.Sin(elevation));
                dirZ.Add(-MathF.Cos(elevation) * MathF.Cos(azimuth));
                logRange.Add(MathF.Log(r));
                rangeSum += r;
            }
        }

        var count = logRange.Count;
        if (count == 0)
            return new RoomBox(0f, 2f, 2f, 2f, 2f, 1.3f, 1.3f);

        var mean = rangeSum / count;
        float Cost(float[] prm)
        {
            for (var k = 1; k < 7; k++)
            {
                if (prm[k] < 0.05f)
                    return float.MaxValue;
            }

            var box = new RoomBox(prm[0], prm[1], prm[2], prm[3], prm[4], prm[5], prm[6]);
            var total = 0f;
            for (var i = 0; i < count; i++)
            {
                var e = MathF.Abs(logRange[i] - MathF.Log(box.RangeAlong(dirX[i], dirY[i], dirZ[i])));
                total += MathF.Min(e, 0.3f);
            }

            return total / count;
        }

        var random = new Random(7);
        var bestParams = new float[7];
        var bestCost = float.MaxValue;
        var current = new float[7];
        var trial = new float[7];
        for (var start = 0; start < 18; start++)
        {
            current[0] = (start % 6) * MathF.PI / 12f;
            for (var k = 1; k < 7; k++)
                current[k] = mean * (0.6f + 1.0f * (float)random.NextDouble());

            var cost = Cost(current);
            var stepSize = new float[7] { 0.15f, 0.25f * mean, 0.25f * mean, 0.25f * mean, 0.25f * mean, 0.25f * mean, 0.25f * mean };
            for (var iteration = 0; iteration < 120; iteration++)
            {
                var improved = false;
                for (var k = 0; k < 7; k++)
                {
                    for (var sign = -1; sign <= 1; sign += 2)
                    {
                        Array.Copy(current, trial, 7);
                        trial[k] += sign * stepSize[k];
                        var c = Cost(trial);
                        if (c < cost)
                        {
                            cost = c;
                            Array.Copy(trial, current, 7);
                            improved = true;
                        }
                    }
                }

                if (!improved)
                {
                    var small = true;
                    for (var k = 0; k < 7; k++)
                    {
                        stepSize[k] *= 0.5f;
                        small &= stepSize[k] < (k == 0 ? 0.002f : 0.002f * mean);
                    }

                    if (small)
                        break;
                }
            }

            if (cost < bestCost)
            {
                bestCost = cost;
                Array.Copy(current, bestParams, 7);
            }
        }

        return new RoomBox(bestParams[0], bestParams[1], bestParams[2], bestParams[3], bestParams[4], bestParams[5], bestParams[6]);
    }

    /** 3x3 median of the range grid (azimuth wraps); cells with fewer than four filled neighbours are removed as specks. */
    private static void MedianFilterRange(float[] range, int width, int height)
    {
        var result = new float[range.Length];
        Array.Fill(result, float.NaN);
        var window = new float[9];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (float.IsNaN(range[y * width + x]))
                    continue;

                var n = 0;
                for (var dy = -1; dy <= 1; dy++)
                {
                    var yy = y + dy;
                    if (yy < 0 || yy >= height)
                        continue;

                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var value = range[yy * width + (x + dx + width) % width];
                        if (!float.IsNaN(value))
                            window[n++] = value;
                    }
                }

                if (n < 4)
                    continue;

                Array.Sort(window, 0, n);
                result[y * width + x] = window[n / 2];
            }
        }

        Array.Copy(result, range, range.Length);
    }

    /** Fills empty cells that have at least three filled 8-neighbours with their mean, a few passes deep. */
    private static void FillSmallHoles(float[] range, System.Numerics.Vector3[] color, int width, int height, int passes)
    {
        var add = new List<(int Index, float Range, System.Numerics.Vector3 Color)>();
        for (var pass = 0; pass < passes; pass++)
        {
            add.Clear();
            for (var y = 1; y < height - 1; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    if (!float.IsNaN(range[y * width + x]))
                        continue;

                    var n = 0;
                    var sum = 0f;
                    var colorSum = System.Numerics.Vector3.Zero;
                    for (var dy = -1; dy <= 1; dy++)
                    {
                        for (var dx = -1; dx <= 1; dx++)
                        {
                            var index = (y + dy) * width + (x + dx + width) % width;
                            if (float.IsNaN(range[index]))
                                continue;

                            n++;
                            sum += range[index];
                            colorSum += color[index];
                        }
                    }

                    if (n >= 3)
                        add.Add((y * width + x, sum / n, colorSum / n));
                }
            }

            if (add.Count == 0)
                break;

            foreach (var item in add)
            {
                range[item.Index] = item.Range;
                color[item.Index] = item.Color;
            }
        }
    }

    private void WritePly(string absolutePath, bool ascii)
    {
        var count = _points.Count;
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("ply");
        builder.AppendLine(ascii ? "format ascii 1.0" : "format binary_little_endian 1.0");
        builder.AppendLine("comment TiXL DepthScan3D");
        builder.AppendLine($"element vertex {count}");
        builder.AppendLine("property float x");
        builder.AppendLine("property float y");

        builder.AppendLine("property float z");
        builder.AppendLine("property uchar red");
        builder.AppendLine("property uchar green");
        builder.AppendLine("property uchar blue");
        builder.AppendLine("end_header");
        var header = builder.ToString();
        if (ascii)
        {
            var ci = CultureInfo.InvariantCulture;
            using (var writer = new StreamWriter(absolutePath, false, new UTF8Encoding(false)))
            {
                writer.Write(header);
                foreach (var sp in _points)
                {
                    writer.WriteLine("{0} {1} {2} {3} {4} {5}",
                                     sp.Position.X.ToString("R", ci), sp.Position.Y.ToString("R", ci), sp.Position.Z.ToString("R", ci),
                                     ToByte(sp.Color.X).ToString(ci), ToByte(sp.Color.Y).ToString(ci), ToByte(sp.Color.Z).ToString(ci));
                }
            }

            return;
        }

        var payload = new byte[count * 15];
        var offset = 0;
        foreach (var sp in _points)
        {
            var xb = BitConverter.GetBytes(sp.Position.X);
            var yb = BitConverter.GetBytes(sp.Position.Y);
            var zb = BitConverter.GetBytes(sp.Position.Z);
            Array.Copy(xb, 0, payload, offset, 4);
            Array.Copy(yb, 0, payload, offset + 4, 4);
            Array.Copy(zb, 0, payload, offset + 8, 4);
            payload[offset + 12] = ToByte(sp.Color.X);
            payload[offset + 13] = ToByte(sp.Color.Y);
            payload[offset + 14] = ToByte(sp.Color.Z);
            offset += 15;
        }

        using (var file = File.Create(absolutePath))
        {
            var headerBytes = System.Text.Encoding.UTF8.GetBytes(header);
            file.Write(headerBytes, 0, headerBytes.Length);
            file.Write(payload, 0, payload.Length);
        }
    }

    private static byte ToByte(float value)
    {
        return (byte)Math.Clamp((int)(value * 255f + 0.5f), 0, 255);
    }

    private const int SeamBins = 200;
    private const float SeamMaxLogRatio = 1f;
    private readonly int[] _seamHistogram = new int[SeamBins];
    private int _seamSamples;

    /** How far each new sample lands from what earlier captures put in the same direction: the scan's own measure of how well it is assembled. */
    private void RecordSeam(float ratio)
    {
        var deviation = MathF.Abs(MathF.Log(Math.Max(ratio, 1e-3f)));
        _seamHistogram[Math.Min((int)(deviation / SeamMaxLogRatio * SeamBins), SeamBins - 1)]++;
        _seamSamples++;
    }

    private bool TryGetSeamError(out float median, out float p90)
    {
        median = p90 = 0f;
        if (_seamSamples < 500)
            return false;

        var seen = 0;
        var gotMedian = false;
        for (var i = 0; i < SeamBins; i++)
        {
            seen += _seamHistogram[i];
            var value = (i + 1) * SeamMaxLogRatio / SeamBins;
            if (!gotMedian && seen >= _seamSamples * 0.5f)
            {
                median = value;
                gotMedian = true;
            }

            if (seen >= _seamSamples * 0.9f)
            {
                p90 = value;
                return true;
            }
        }

        return true;
    }

    private void UpdateStatus()
    {
        if (_points.Count == 0)
        {
            SetStatus("Enable Capture or StartRecord to scan points into the cloud.", IStatusProvider.StatusLevel.Notice);
            return;
        }

        var state = _recording ? "Recording" : "Holding";
        var camMode = _pendingCamIsPose ? "world space (DA3 camera pose)" : "camera space - connect CamReference for world space";
        var anchorText = _pendingAutoScale && _anchorScale != 1f ? $", scale x{_anchorScale:0.###}" : string.Empty;
        var seamText = TryGetSeamError(out var seamMedian, out var seamP90) ? $", seam error {seamMedian * 100f:0}% (90th {seamP90 * 100f:0}%)" : string.Empty;
        SetStatus($"{state} scan: {_points.Count} points, {_captures} captures{anchorText}{seamText}, {camMode}.", IStatusProvider.StatusLevel.Success);
    }

    /// <summary>How overlapping voxel observations merge; values only ever appended.</summary>
    public enum MergeMode
    {
        Average,
        Replace,
    }

    /// <summary>PLY output encoding; values only ever appended.</summary>
    public enum PlyFormat
    {
        Ascii,
        Binary,
        Mesh,
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

    #region Inputs

    [Input(Guid = "93c8d941-f395-4271-b36a-86aadb010cff")]
    public readonly InputSlot<Texture2D?> DepthMap = new();

    [Input(Guid = "afc191c7-be4d-44eb-938b-701e35f75fae")]
    public readonly InputSlot<Texture2D?> ColorMap = new();

    [Input(Guid = "79ecd596-22d3-44a1-8f25-5fd74549770d")]
    public readonly InputSlot<Texture2D?> Confidence = new();

    [Input(Guid = "a7a2eb05-619f-4363-9b6a-120d3c4d0411")]
    public readonly InputSlot<object> CameraReference = new();

    [Input(Guid = "d31b9eb4-076e-460d-9204-25247df35875")]
    public readonly InputSlot<float> FieldOfView = new(50f);

    [Input(Guid = "c0f6dcfb-f8d9-4030-80da-ca0f7042e936")]
    public readonly InputSlot<float> PointScale = new(1f);

    [Input(Guid = "c84c32af-1f72-4ade-badd-83525ba48332")]
    public readonly InputSlot<float> VoxelSize = new(0.02f);

    [Input(Guid = "9fa379e9-10b0-4010-a344-2bba6a85fcd0", MappedType = typeof(MergeMode))]
    public readonly InputSlot<int> Mode = new((int)MergeMode.Average);

    [Input(Guid = "fc20b574-76b5-4acd-bcec-933960de8f6a")]
    public readonly InputSlot<bool> Capture = new(false);

    [Input(Guid = "34d31350-9754-43bf-8037-aa1f7f1a6eda")]
    public readonly InputSlot<bool> CaptureOnce = new(false);

    [Input(Guid = "79d30636-4d7f-4c46-97b6-806b20d8f674")]
    public readonly InputSlot<bool> StartRecord = new(false);

    [Input(Guid = "b080c2a6-1559-4dcb-837d-24e68f4db439")]
    public readonly InputSlot<bool> StopRecord = new(false);

    [Input(Guid = "e8ba2ceb-4817-47b9-a2e2-f86cf73f2a6a")]
    public readonly InputSlot<bool> Reset = new(false);

    [Input(Guid = "5304c51a-4e3f-4d9c-a34b-4af6f5cbe0d9")]
    public readonly InputSlot<int> MaxPoints = new(500000);

    [Input(Guid = "5a05aca8-d4ed-43b0-82e4-1dcd41849203")]
    public readonly InputSlot<float> SampleLifetime = new(0f);

    [Input(Guid = "0187ea06-b66b-4cc2-9fd1-7d68455ac6b9")]
    public readonly InputSlot<int> PixelStride = new(2);

    [Input(Guid = "e805a254-7980-4bbe-b603-d58e3a010105")]
    public readonly InputSlot<float> ConfidenceThreshold = new(0.5f);

    [Input(Guid = "8a4f0d63-2e91-4b57-a3c8-6f5d1e2b4c70")]
    public readonly InputSlot<bool> AutoScale = new(true);

    [Input(Guid = "b4c8e1a2-5d37-4f90-8a6b-3e2f1c7d9a40")]
    public readonly InputSlot<bool> RegularizeRoom = new(false);

    [Input(Guid = "d7a3f5c1-2b84-4e96-9c1d-6f0a8b3e5d72")]
    public readonly InputSlot<bool> GlobalAlign = new(false);

    [Input(Guid = "e9b4c7a3-6d15-4b82-a3f0-1c8d5e7b2a96")]
    public readonly InputSlot<bool> FlatSurfaces = new(true);

    [Input(Guid = "3c5e7a91-4d28-4f6b-8a13-7e9d2c4b6f01")]
    public readonly InputSlot<float> KnownFloorSizeA = new(0f);

    [Input(Guid = "9a1d3f57-6b82-4c04-b5e9-2f7a8c1d3e64")]
    public readonly InputSlot<float> KnownFloorSizeB = new(0f);

    [Input(Guid = "6c269444-aaec-4359-9708-4eb2cfdd0487")]
    public readonly InputSlot<bool> SaveTrigger = new(false);

    [Input(Guid = "75c48797-6bba-4176-9549-459e9bcac012")]
    public readonly InputSlot<string> SaveFolder = new("Resources/DepthScans");

    [Input(Guid = "0773ed04-80ce-43b1-9104-990d44f9ba33")]
    public readonly InputSlot<string> SaveFileName = new("scan3d");

    [Input(Guid = "13b697b4-c85d-4a15-afb2-37d76a8425e9", MappedType = typeof(PlyFormat))]
    public readonly InputSlot<int> SaveFormatInput = new((int)PlyFormat.Binary);

    #endregion

    #region Private Fields

    private struct ScanPoint
    {
        public System.Numerics.Vector3 Position;
        public System.Numerics.Vector3 Color;
        public int Count;
        public float Weight;
        public float LastSeen;
    }

    private const int ReadbackTimeoutFrames = 60;
    private const int AnchorSampleCap = 400;
    private const int MinAnchorMatches = 12;
    private const int MinAnchorPoints = 300;
    private const float SphericalCellRadians = 0.25f * MathF.PI / 180f;
    private const float SphericalAgreeLow = 0.92f;
    private const float SphericalAgreeHigh = 1.08f;
    private const int SphericalMinObservations = 2;
    private const float FlyingPixelRatio = 0.08f;
    private const float MeshDepthJumpRatio = 1.6f;
    private const float MeshScanJumpRatio = 1.25f;
    private const int TileStride = 3;

    /** Neighbouring points of a rotation-only scan are an angle apart, so their spacing grows with range; a point drawn at this share of its range just covers it. DrawPoints' default PointSize of 0.1 turns the factor of ten back into 1:1. */
    private const float PointScalePerRange = 2f * SphericalCellRadians * 1.35f * 10f;
    private const int MaxTiles = 4000;
    private const int MinTilesForAlignment = 8;
    private const int MeshSmallHoleEdges = 400;
    private const float RoomObjectRatio = 0.75f;
    private const int MeshRimLoopEdges = 2000;

    private List<ScanPoint> _points = [];
    private Dictionary<long, int> _voxelIndex = [];
    private readonly System.Diagnostics.Stopwatch _scanClock = System.Diagnostics.Stopwatch.StartNew();
    private int _captures;
    private int _pointsAdded;
    private bool _bufferFullLogged;
    private BufferWithViews? _pointBuffer;

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
    private int _stagingWidth;
    private int _stagingHeight;
    private int _colorStagingWidth;
    private int _colorStagingHeight;
    private bool _pendingColorIsBgra;

    private System.Numerics.Matrix4x4 _pendingCamToWorld;
    private float _pendingTanHalfX;
    private float _pendingTanHalfY;
    private bool _pendingCamIsPose;
    private int _skippedLost;
    private ScanPoint[] _snapshotBuffer = [];
    private Point[] _shellBuffer = [];
    private int _shellCount;
    private bool _pendingSpherical;
    /** One capture's raw samples on the spherical grid: the unit of the global scale alignment. */
    private sealed class ScanTile
    {
        public int[] Cell = [];
        public float[] LogRange = [];
        public uint[] Color = [];
        public float[] Weight = [];
    }

    private readonly List<ScanTile> _tiles = [];
    private readonly List<int> _tileCells = [];
    private readonly List<float> _tileLogRanges = [];
    private readonly List<uint> _tileColors = [];
    private readonly List<float> _tileWeights = [];
    private volatile bool _meshBusy;
    private volatile bool _shellBusy;
    private volatile bool _shellReady;
    private volatile int _shellGeneration;
    private volatile Point[]? _shellPoints;
    private int _shellCaptures;
    private bool _shellAligned;
    private bool _shellRegularize;
    private bool _shellFlat;
    private long _lastMergeTicks;
    private long _lastShellTicks;
    private static readonly long ShellMinIntervalTicks = 2 * System.Diagnostics.Stopwatch.Frequency;
    private static readonly long FlattenIdleTicks = (long)(1.5 * System.Diagnostics.Stopwatch.Frequency);

    private bool RegularizeUnchanged() => _shellRegularize == _pendingRegularize && _shellFloorA == _pendingFloorA && _shellFloorB == _pendingFloorB;
    private volatile bool _meshDone;
    private volatile string _meshResultPath = string.Empty;
    private volatile string _meshResultMessage = string.Empty;
    private MergeMode _pendingMode;
    private float _pendingThreshold;
    private float _pendingScale;
    private float _pendingVoxelSize;
    private int _pendingStride;
    private int _pendingMaxPoints;
    private bool _pendingAutoScale;
    private bool _pendingRegularize;
    private bool _pendingGlobalAlign;
    private float _pendingFloorA;
    private float _pendingFloorB;
    private float _shellFloorA;
    private float _shellFloorB;
    private bool _pendingFlat;
    private float _anchorScale = 1f;
    private bool _captureOnceState;
    private bool _resetState;
    private bool _saveState;
    private bool _startRecordState;
    private bool _stopRecordState;
    private bool _recording;
    private bool _loggedWrongFormat;
    private bool _loggedConfMismatch;
    private bool _loggedColorMismatch;
    private string? _statusMessage;
    private IStatusProvider.StatusLevel _statusLevel;
    private string? _lastLoggedStatus;
    private int _uiMode;
    private int _uiSaveFormat;

    private static readonly string[] ModeNames = { "Average", "Replace" };
    private static readonly string[] SaveFormatNames = { "PLY ASCII", "PLY binary", "PLY mesh" };

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

        _pointBuffer?.Dispose();
        _pointBuffer = null;
        _depthStaging?.Dispose();
        _depthStaging = null;
        _confStaging?.Dispose();
        _confStaging = null;
        _colorStaging?.Dispose();
        _colorStaging = null;
        base.Dispose(isDisposing);
    }
}
