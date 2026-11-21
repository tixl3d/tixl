#nullable enable
using Lib.Utils;
using T3.Core.Utils;

namespace Lib.point.io;

/// <summary>
/// Loads a CSV file and routes its columns into the attributes of a point buffer.
/// </summary>
/// <remarks>
/// <para>
/// Columns are matched by header name, so their order in the file does not matter. Unmapped
/// attributes keep their point defaults. The file is cached: it is re-read when <c>FilePath</c>
/// changes, when <c>Reload</c> flanks, or when a column mapping changes (which rebuilds from the
/// already parsed rows without touching the disk).
/// </para>
/// <para>
/// Threading: <see cref="Update"/> runs on the evaluation thread; <see cref="ICustomDropdownHolder"/>
/// methods run on the UI thread. The shared state is limited to the published column-name snapshot,
/// the header source-path cache and the status snapshot. The arrays and status objects are immutable
/// and replaced as a whole, never mutated, so the UI can enumerate them while the evaluation thread
/// swaps them; claim and publish order is serialized by a small lock.
/// Everything else (parsed rows, header lookup, GPU buffer, applied mapping) is owned by the
/// evaluation thread.
/// </para>
/// <para>
/// Robustness: malformed cells fall back to attribute defaults, non-finite numbers are rejected,
/// file errors are reported through <see cref="IStatusProvider"/> instead of throwing, and the
/// parse/upload path is wrapped so a bad file cannot take the editor down. Files above 128 MiB and
/// files with more than <c>MaxPointCount</c> data rows are refused with a status message before a
/// point array is allocated.
/// </para>
/// </remarks>
[Guid("3e2b155e-52cd-4a58-9b7c-602d7a36e19c")]
internal sealed class CsvToPoints : Instance<CsvToPoints>, IStatusProvider, ICustomDropdownHolder
{
    public enum RotationModes
    {
        EulerXYZ,
        Quaternion,
    }

    private struct ColumnMap : IEquatable<ColumnMap>
    {
        public int PositionX;
        public int PositionY;
        public int PositionZ;
        public int RotationW;
        public int RotationX;
        public int RotationY;
        public int RotationZ;
        public int ColorR;
        public int ColorG;
        public int ColorB;
        public int ColorA;
        public int ScaleX;
        public int ScaleY;
        public int ScaleZ;
        public int F1;
        public int F2;

        public bool Equals(ColumnMap other)
        {
            return PositionX == other.PositionX
                   && PositionY == other.PositionY
                   && PositionZ == other.PositionZ
                   && RotationW == other.RotationW
                   && RotationX == other.RotationX
                   && RotationY == other.RotationY
                   && RotationZ == other.RotationZ
                   && ColorR == other.ColorR
                   && ColorG == other.ColorG
                   && ColorB == other.ColorB
                   && ColorA == other.ColorA
                   && ScaleX == other.ScaleX
                   && ScaleY == other.ScaleY
                   && ScaleZ == other.ScaleZ
                   && F1 == other.F1
                   && F2 == other.F2;
        }

        public override bool Equals(object? obj)
        {
            return obj is ColumnMap other && Equals(other);
        }

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(PositionX);
            hash.Add(PositionY);
            hash.Add(PositionZ);
            hash.Add(RotationW);
            hash.Add(RotationX);
            hash.Add(RotationY);
            hash.Add(RotationZ);
            hash.Add(ColorR);
            hash.Add(ColorG);
            hash.Add(ColorB);
            hash.Add(ColorA);
            hash.Add(ScaleX);
            hash.Add(ScaleY);
            hash.Add(ScaleZ);
            hash.Add(F1);
            hash.Add(F2);
            return hash.ToHashCode();
        }
    }

    [Output(Guid = "013eb3d8-8e97-44fc-83e4-c777231d8434")]
    public readonly Slot<BufferWithViews?> Points = new();

    [Input(Guid = "537605b3-5d85-4989-bf95-3324107b34c7")]
    public readonly InputSlot<string> FilePath = new();

    [Input(Guid = "d8311eeb-ab1e-4d51-bb6c-3a416868a4a5")]
    public readonly InputSlot<bool> Reload = new();

    [Input(Guid = "20802e48-71d9-4636-b47a-dc0d3271306a", MappedType = typeof(RotationModes))]
    public readonly InputSlot<int> RotationMode = new();

    [Input(Guid = "dce9bb44-a12e-4c28-876d-d6fd7c19af16")]
    public readonly InputSlot<string> RotationW = new();

    [Input(Guid = "e03929c8-b50f-4c46-b283-74b76d1fcc58")]
    public readonly InputSlot<string> RotationX = new();

    [Input(Guid = "a1037cf2-10db-432e-a65d-526e2cd58583")]
    public readonly InputSlot<string> RotationY = new();

    [Input(Guid = "074a03c5-08a8-4edc-9906-edef98dbb754")]
    public readonly InputSlot<string> RotationZ = new();

    [Input(Guid = "1b668fd3-5ad8-4e50-8ad3-359d3de9b404")]
    public readonly InputSlot<string> PositionX = new();

    [Input(Guid = "1074083c-0ede-4821-9a64-381fc19502dc")]
    public readonly InputSlot<string> PositionY = new();

    [Input(Guid = "ba625921-0efd-49f9-b7f5-b335bb39c6c3")]
    public readonly InputSlot<string> PositionZ = new();

    [Input(Guid = "7854f719-d7a4-42c5-b52d-22ae1f0e76b1")]
    public readonly InputSlot<string> ColorR = new();

    [Input(Guid = "723a2460-2db4-485d-9763-42f00c987350")]
    public readonly InputSlot<string> ColorG = new();

    [Input(Guid = "4cf2dead-6843-4ec6-a8b7-a639b4bb63f4")]
    public readonly InputSlot<string> ColorB = new();

    [Input(Guid = "1ad9c3d6-5497-4be4-8f3d-ed935ca7e1ae")]
    public readonly InputSlot<string> ColorA = new();

    [Input(Guid = "c2d008e4-5553-465c-a9fc-b8c7d41f2296")]
    public readonly InputSlot<bool> ColorIs0To255 = new();

    [Input(Guid = "bd3a1311-caee-4283-83a4-59b93a971d38")]
    public readonly InputSlot<string> ScaleX = new();

    [Input(Guid = "cc323942-e0e7-4a66-84d3-066fa23c3ed7")]
    public readonly InputSlot<string> ScaleY = new();

    [Input(Guid = "73b148ac-4669-4738-b18d-4790adf174ba")]
    public readonly InputSlot<string> ScaleZ = new();

    [Input(Guid = "eaefda89-f7ad-4456-be37-ed9d4e6b0245")]
    public readonly InputSlot<string> F1 = new();

    [Input(Guid = "2d59cb93-d544-42c9-ade4-2f871110ca0d")]
    public readonly InputSlot<string> F2 = new();

    public CsvToPoints()
    {
        Points.UpdateAction += Update;
    }

    IStatusProvider.StatusLevel IStatusProvider.GetStatusLevel()
    {
        return _status.Level;
    }

    string? IStatusProvider.GetStatusMessage()
    {
        return _status.Message;
    }

    string? ICustomDropdownHolder.GetValueForInput(Guid inputId)
    {
        return GetMappingSlot(inputId)?.Value ?? string.Empty;
    }

    IEnumerable<string> ICustomDropdownHolder.GetOptionsForInput(Guid inputId)
    {
        var path = FilePath.Value ?? string.Empty;
        if (!string.Equals(path, _headerSourcePath, StringComparison.Ordinal))
        {
            LoadColumnNamesFromHeader(path);
        }

        // The published snapshot is never mutated; returning it directly is safe across threads.
        return _columnOptions;
    }

    void ICustomDropdownHolder.HandleResultForInput(Guid inputId, string? selected, bool isAListItem)
    {
        var slot = GetMappingSlot(inputId);
        if (slot == null || selected == null)
        {
            return;
        }

        slot.SetTypedInputValue(selected == NoColumnOption ? string.Empty : selected);
    }

    private void Update(EvaluationContext context)
    {
        try
        {
            UpdateInternal(context);
        }
        catch (Exception exception)
        {
            // User data must never be able to take the editor down. Report the failure and keep
            // the output empty; pressing Reload retries the file.
            LogError($"Failed to load CSV: {exception.Message}");
            UpdatePointBuffer(null);
            SetStatus($"Failed to load CSV: {exception.Message}", IStatusProvider.StatusLevel.Error);
            Points.Value = _pointBuffer;
        }
    }

    private void UpdateInternal(EvaluationContext context)
    {
        var reloadRequested = MathUtils.WasTriggered(Reload.GetValue(context), ref _reloadTriggered);
        if (reloadRequested)
        {
            Reload.SetTypedInputValue(false);
        }

        var path = FilePath.GetValue(context) ?? string.Empty;
        var rotationMode = (RotationModes)RotationMode.GetValue(context);
        var colorIs0To255 = ColorIs0To255.GetValue(context);

        if (string.IsNullOrWhiteSpace(path))
        {
            if (_columnOptions.Length > 1 || !string.IsNullOrEmpty(_headerSourcePath))
            {
                InvalidateColumnOptions();
            }

            _loadedPath = string.Empty;
            _rows = null;
            _headers.Clear();
            _headerIndices.Clear();
            _lastLoggedError = string.Empty;
            _appliedMap = UnmappedColumns;
            _appliedRotationMode = rotationMode;
            _appliedColorIs0To255 = colorIs0To255;
            UpdatePointBuffer(null);
            SetStatus("No CSV file selected.", IStatusProvider.StatusLevel.Notice);
            Points.Value = _pointBuffer;
            return;
        }

        var fileChanged = reloadRequested || !string.Equals(path, _loadedPath, StringComparison.Ordinal);

        if (fileChanged)
        {
            if (LoadFile(path))
            {
                _appliedMap = ResolveColumns(context);
                _appliedRotationMode = rotationMode;
                _appliedColorIs0To255 = colorIs0To255;
                RebuildPoints(_appliedMap, rotationMode, colorIs0To255);
            }
            else
            {
                _appliedMap = UnmappedColumns;
                _appliedRotationMode = rotationMode;
                _appliedColorIs0To255 = colorIs0To255;
            }
        }
        else
        {
            var map = ResolveColumns(context);
            if (!map.Equals(_appliedMap)
                || rotationMode != _appliedRotationMode
                || colorIs0To255 != _appliedColorIs0To255)
            {
                _appliedMap = map;
                _appliedRotationMode = rotationMode;
                _appliedColorIs0To255 = colorIs0To255;
                RebuildPoints(map, rotationMode, colorIs0To255);
            }
        }

        Points.Value = _pointBuffer;
    }

    /// <summary>
    /// Parses the whole file into rows. Sets <c>_loadedPath</c> even on failure so the file is not
    /// re-read on every frame; an explicit <c>Reload</c> flank retries it.
    /// </summary>
    /// <remarks>Evaluation thread only.</remarks>
    private bool LoadFile(string path)
    {
        _loadedPath = path;
        InvalidateColumnOptions();
        _rows = null;
        _headers.Clear();
        _headerIndices.Clear();
        _lastLoggedError = string.Empty;

        if (!TryGetFilePath(path, out var resolvedPath))
        {
            LogWarn($"File not found: {path}");
            UpdatePointBuffer(null);
            SetStatus($"File not found: {path}", IStatusProvider.StatusLevel.Warning);
            return false;
        }

        try
        {
            // One stream for the length check and the read, so the length cannot drift between them.
            using var stream = new FileStream(resolvedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaxCsvFileSizeInBytes)
            {
                var megabyteLimit = MaxCsvFileSizeInBytes / (1024 * 1024);
                var message = $"The CSV is {stream.Length / (1024f * 1024f):0.0} MB; files above {megabyteLimit} MB are not loaded to protect memory.";
                LogWarn(message);
                UpdatePointBuffer(null);
                SetStatus(message, IStatusProvider.StatusLevel.Error);
                return false;
            }

            using var reader = new StreamReader(stream);
            var rows = CsvPointParser.Parse(reader.ReadToEnd(), MaxPointCount + 1, out var exceededRowLimit);
            if (exceededRowLimit)
            {
                var message = $"The CSV has more than {MaxPointCount} data rows; only buffers of up to {MaxPointCount} points are supported.";
                LogWarn(message);
                UpdatePointBuffer(null);
                SetStatus(message, IStatusProvider.StatusLevel.Error);
                return false;
            }

            if (rows.Count == 0)
            {
                LogWarn("The CSV file is empty.");
                UpdatePointBuffer(null);
                SetStatus("The CSV file is empty.", IStatusProvider.StatusLevel.Warning);
                return false;
            }

            _headers.AddRange(rows[0]);
            rows.RemoveAt(0);

            for (var i = 0; i < _headers.Count; i++)
            {
                var header = CsvPointParser.NormalizeHeader(_headers[i]);
                _headers[i] = header;
                if (header.Length > 0)
                {
                    _headerIndices.TryAdd(header, i);
                }
            }

            _rows = rows;
            PublishColumnOptions(path, _headers);
            return true;
        }
        catch (Exception exception)
        {
            LogError($"Failed to read CSV: {exception.Message}");
            UpdatePointBuffer(null);
            SetStatus($"Failed to read CSV: {exception.Message}", IStatusProvider.StatusLevel.Error);
            return false;
        }
    }

    /// <summary>
    /// Reads only the header line for the column dropdown. Runs on the UI thread and must not touch
    /// evaluation-thread state; the result is published as a new immutable snapshot under the
    /// header lock, so an older request can never overwrite a newer one.
    /// </summary>
    private void LoadColumnNamesFromHeader(string path)
    {
        int version;
        lock (_headerLock)
        {
            version = ++_headerVersion;
            _headerSourcePath = path;
        }

        var options = new[] { NoColumnOption };
        var hasResult = false;

        if (string.IsNullOrWhiteSpace(path))
        {
            hasResult = true; // No file: the dropdown offers only "(none)".
        }
        else if (TryGetFilePath(path, out var resolvedPath))
        {
            try
            {
                using var reader = new StreamReader(resolvedPath);
                var firstLine = reader.ReadLine();
                if (firstLine == null)
                {
                    hasResult = true; // Empty file: no columns to offer.
                }
                else
                {
                    var rows = CsvPointParser.Parse(firstLine);
                    if (rows.Count > 0)
                    {
                        options = BuildColumnOptions(rows[0]);
                    }

                    hasResult = true;
                }
            }
            catch
            {
                // The evaluation pass reports unreadable files; the dropdown just stays empty here.
            }
        }

        lock (_headerLock)
        {
            if (version != _headerVersion)
            {
                return; // A newer request or a full load owns the snapshot now.
            }

            if (!string.Equals(_headerSourcePath, path, StringComparison.Ordinal))
            {
                return; // Another path was claimed while this header was being read.
            }

            if (hasResult)
            {
                _columnOptions = options;
            }
            else
            {
                // Release the claim so a later frame can retry a file that was briefly unavailable.
                _headerSourcePath = string.Empty;
            }
        }
    }

    private void InvalidateColumnOptions()
    {
        lock (_headerLock)
        {
            _headerVersion++;
            _headerSourcePath = string.Empty;
            _columnOptions = new[] { NoColumnOption };
        }
    }

    private void PublishColumnOptions(string path, IReadOnlyList<string> headers)
    {
        var options = BuildColumnOptions(headers);

        lock (_headerLock)
        {
            // A newer UI request for another path wins; keep its snapshot instead.
            if (_headerSourcePath.Length > 0 && !string.Equals(_headerSourcePath, path, StringComparison.Ordinal))
            {
                return;
            }

            _headerVersion++;
            _headerSourcePath = path;
            _columnOptions = options;
        }
    }

    private static string[] BuildColumnOptions(IReadOnlyList<string> headers)
    {
        var options = new string[headers.Count + 1];
        options[0] = NoColumnOption;
        var count = 1;

        for (var i = 0; i < headers.Count; i++)
        {
            var header = CsvPointParser.NormalizeHeader(headers[i]);
            if (header.Length > 0)
            {
                options[count++] = header;
            }
        }

        if (count != options.Length)
        {
            Array.Resize(ref options, count);
        }

        return options;
    }

    private ColumnMap ResolveColumns(EvaluationContext context)
    {
        return new ColumnMap
                   {
                       PositionX = ResolveIndex(PositionX.GetValue(context)),
                       PositionY = ResolveIndex(PositionY.GetValue(context)),
                       PositionZ = ResolveIndex(PositionZ.GetValue(context)),
                       RotationW = ResolveIndex(RotationW.GetValue(context)),
                       RotationX = ResolveIndex(RotationX.GetValue(context)),
                       RotationY = ResolveIndex(RotationY.GetValue(context)),
                       RotationZ = ResolveIndex(RotationZ.GetValue(context)),
                       ColorR = ResolveIndex(ColorR.GetValue(context)),
                       ColorG = ResolveIndex(ColorG.GetValue(context)),
                       ColorB = ResolveIndex(ColorB.GetValue(context)),
                       ColorA = ResolveIndex(ColorA.GetValue(context)),
                       ScaleX = ResolveIndex(ScaleX.GetValue(context)),
                       ScaleY = ResolveIndex(ScaleY.GetValue(context)),
                       ScaleZ = ResolveIndex(ScaleZ.GetValue(context)),
                       F1 = ResolveIndex(F1.GetValue(context)),
                       F2 = ResolveIndex(F2.GetValue(context)),
                   };
    }

    private int ResolveIndex(string? columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName))
        {
            return -1;
        }

        return _headerIndices.TryGetValue(columnName, out var index) ? index : -1;
    }

    /// <remarks>Evaluation thread only.</remarks>
    private void RebuildPoints(in ColumnMap map, RotationModes rotationMode, bool colorIs0To255)
    {
        var rows = _rows;
        if (rows == null || rows.Count == 0)
        {
            LogWarn("The CSV has no data rows.");
            UpdatePointBuffer(null);
            SetStatus("The CSV has no data rows.", IStatusProvider.StatusLevel.Warning);
            return;
        }

        if (rows.Count > MaxPointCount)
        {
            LogWarn($"The CSV has {rows.Count} rows; a point buffer supports at most {MaxPointCount}.");
            UpdatePointBuffer(null);
            SetStatus($"The CSV has {rows.Count} rows; a point buffer supports at most {MaxPointCount}.",
                      IStatusProvider.StatusLevel.Error);
            return;
        }

        var isQuaternion = rotationMode == RotationModes.Quaternion;
        var points = new Point[rows.Count];
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            points[i] = new Point
                            {
                                Position = new Vector3(CsvPointParser.GetFloat(row, map.PositionX, 0),
                                                       CsvPointParser.GetFloat(row, map.PositionY, 0),
                                                       CsvPointParser.GetFloat(row, map.PositionZ, 0)),
                                F1 = CsvPointParser.GetFloat(row, map.F1, 1),
                                F2 = CsvPointParser.GetFloat(row, map.F2, 1),
                                Color = new Vector4(CsvPointParser.GetColor(row, map.ColorR, colorIs0To255),
                                                    CsvPointParser.GetColor(row, map.ColorG, colorIs0To255),
                                                    CsvPointParser.GetColor(row, map.ColorB, colorIs0To255),
                                                    CsvPointParser.GetColor(row, map.ColorA, colorIs0To255)),
                                Scale = new Vector3(CsvPointParser.GetFloat(row, map.ScaleX, 1),
                                                    CsvPointParser.GetFloat(row, map.ScaleY, 1),
                                                    CsvPointParser.GetFloat(row, map.ScaleZ, 1)),
                                Orientation = CsvPointParser.BuildOrientation(CsvPointParser.GetFloat(row, map.RotationW, 1),
                                                                              CsvPointParser.GetFloat(row, map.RotationX, 0),
                                                                              CsvPointParser.GetFloat(row, map.RotationY, 0),
                                                                              CsvPointParser.GetFloat(row, map.RotationZ, 0),
                                                                              isQuaternion),
                            };
        }

        UpdatePointBuffer(points);
        SetStatus($"Loaded {points.Length} points · {CountMapped(map)} attributes mapped", IStatusProvider.StatusLevel.Success);
    }

    private static int CountMapped(in ColumnMap map)
    {
        var count = 0;
        if (map.PositionX >= 0) count++;
        if (map.PositionY >= 0) count++;
        if (map.PositionZ >= 0) count++;
        if (map.RotationW >= 0) count++;
        if (map.RotationX >= 0) count++;
        if (map.RotationY >= 0) count++;
        if (map.RotationZ >= 0) count++;
        if (map.ColorR >= 0) count++;
        if (map.ColorG >= 0) count++;
        if (map.ColorB >= 0) count++;
        if (map.ColorA >= 0) count++;
        if (map.ScaleX >= 0) count++;
        if (map.ScaleY >= 0) count++;
        if (map.ScaleZ >= 0) count++;
        if (map.F1 >= 0) count++;
        if (map.F2 >= 0) count++;
        return count;
    }

    /// <summary>
    /// Uploads the points, reusing the GPU buffer while the point count is unchanged.
    /// </summary>
    /// <remarks>
    /// Evaluation thread only. A count change disposes and recreates the buffer because a structured
    /// buffer cannot be resized in place; downstream operators re-read the slot every frame.
    /// </remarks>
    private void UpdatePointBuffer(Point[]? points)
    {
        var count = points?.Length ?? 0;
        if (count == 0)
        {
            _pointBuffer?.Dispose();
            _pointBuffer = null;
            return;
        }

        var currentCount = _pointBuffer?.Buffer != null ? _pointBuffer.Buffer.Description.SizeInBytes / Point.Stride : 0;
        if (count != currentCount)
        {
            _pointBuffer?.Dispose();
            _pointBuffer = null;
        }

        if (_pointBuffer == null)
        {
            _pointBuffer = new BufferWithViews();
            ResourceManager.SetupStructuredBuffer(points!, Point.Stride * count, Point.Stride, ref _pointBuffer.Buffer);
            ResourceManager.CreateStructuredBufferSrv(_pointBuffer.Buffer, ref _pointBuffer.Srv);
            ResourceManager.CreateStructuredBufferUav(_pointBuffer.Buffer, UnorderedAccessViewBufferFlags.None, ref _pointBuffer.Uav);
        }
        else
        {
            ResourceManager.Device.ImmediateContext.UpdateSubresource(points!, _pointBuffer.Buffer!);
        }
    }

    private void SetStatus(string message, IStatusProvider.StatusLevel level)
    {
        // One immutable snapshot keeps the message and level consistent for a reader.
        _status = new StatusSnapshot(message, level);
    }

    private void LogWarn(string message)
    {
        Log.Warning($"CsvToPoints: {message}", this);
    }

    private void LogError(string message)
    {
        // File failures happen once per load; the catch-all can repeat every frame. Log the first
        // occurrence of a message and stay quiet while it persists.
        if (string.Equals(_lastLoggedError, message, StringComparison.Ordinal))
        {
            return;
        }

        _lastLoggedError = message;
        Log.Error($"CsvToPoints: {message}", this);
    }

    private InputSlot<string>? GetMappingSlot(Guid inputId)
    {
        if (inputId == PositionX.Id) return PositionX;
        if (inputId == PositionY.Id) return PositionY;
        if (inputId == PositionZ.Id) return PositionZ;
        if (inputId == RotationW.Id) return RotationW;
        if (inputId == RotationX.Id) return RotationX;
        if (inputId == RotationY.Id) return RotationY;
        if (inputId == RotationZ.Id) return RotationZ;
        if (inputId == ColorR.Id) return ColorR;
        if (inputId == ColorG.Id) return ColorG;
        if (inputId == ColorB.Id) return ColorB;
        if (inputId == ColorA.Id) return ColorA;
        if (inputId == ScaleX.Id) return ScaleX;
        if (inputId == ScaleY.Id) return ScaleY;
        if (inputId == ScaleZ.Id) return ScaleZ;
        if (inputId == F1.Id) return F1;
        if (inputId == F2.Id) return F2;
        return null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pointBuffer?.Dispose();
            _pointBuffer = null;
            _rows = null;
            _headers.Clear();
            _headerIndices.Clear();
        }

        base.Dispose(disposing);
    }

    private static readonly ColumnMap UnmappedColumns = new()
                                                            {
                                                                PositionX = -1,
                                                                PositionY = -1,
                                                                PositionZ = -1,
                                                                RotationW = -1,
                                                                RotationX = -1,
                                                                RotationY = -1,
                                                                RotationZ = -1,
                                                                ColorR = -1,
                                                                ColorG = -1,
                                                                ColorB = -1,
                                                                ColorA = -1,
                                                                ScaleX = -1,
                                                                ScaleY = -1,
                                                                ScaleZ = -1,
                                                                F1 = -1,
                                                                F2 = -1,
                                                            };

    private const string NoColumnOption = "(none)";

    /// <summary>Refuse files that would need more memory than the editor should reserve for one op.</summary>
    private const long MaxCsvFileSizeInBytes = 128L * 1024 * 1024;

    /// <summary>
    /// Upper bound for one point buffer. Also keeps <see cref="Point.Stride"/> * count inside an
    /// <see cref="int"/>; the parser stops after this many data rows.
    /// </summary>
    private const int MaxPointCount = 2_000_000;

    /// <summary>Reports a status message and level as one consistent pair.</summary>
    private sealed class StatusSnapshot
    {
        public readonly string Message;
        public readonly IStatusProvider.StatusLevel Level;

        public StatusSnapshot(string message, IStatusProvider.StatusLevel level)
        {
            Message = message;
            Level = level;
        }
    }

    // Shared with the UI thread. The options array is published as a whole, never mutated.
    private readonly object _headerLock = new();
    private volatile string[] _columnOptions = new[] { NoColumnOption };
    private volatile string _headerSourcePath = string.Empty;

    // Guards the publish order of _columnOptions; only touched inside _headerLock.
    private int _headerVersion;

    // Evaluation-thread-only state.
    private readonly List<string> _headers = new();
    private readonly Dictionary<string, int> _headerIndices = new(StringComparer.OrdinalIgnoreCase);
    private List<string[]>? _rows;
    private string _loadedPath = string.Empty;
    private BufferWithViews? _pointBuffer;
    private ColumnMap _appliedMap = UnmappedColumns;
    private RotationModes _appliedRotationMode;
    private bool _appliedColorIs0To255;
    private bool _reloadTriggered;
    private string _lastLoggedError = string.Empty;

    // Read by the UI thread; replaced atomically on the evaluation thread.
    private volatile StatusSnapshot _status = new("No CSV file selected.", IStatusProvider.StatusLevel.Notice);
}
