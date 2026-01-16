#nullable enable
using System.Globalization;
using System.Text.Json;
using T3.Core.Utils;

namespace Lib.point.io;

/// <summary>
///     Converts point data from CSV or JSON files into a GPU structured buffer of <see cref="Point" /> objects.
///     It supports custom column mapping for CSV files and can export the converted points back to CSV or JSON.
/// </summary>
[Guid("a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d")]
public sealed class DataPointConverter : Instance<DataPointConverter>
{
    [Input(Guid = "3c4d5e6f-7a8b-4c1d-8e9f-3a4b5c6d7e8f")]
    public readonly InputSlot<bool> Convert = new();

    [Input(Guid = "d5f6a7b8-c9d0-4e1f-2a3b-8c7d6e5f4a3b")]
    public readonly InputSlot<string> CsvF1Mapping = new("F1");

    [Input(Guid = "4c7e6d5a-8f9b-4c1d-9e2a-3b4c5d6e7f8a")]
    public readonly InputSlot<string> CsvPosXMapping = new("Position X");

    [Input(Guid = "5d6f7a8b-9c1d-4e2f-8a3b-5c4d6e7f8a9b")]
    public readonly InputSlot<string> CsvPosYMapping = new("Position Y");

    [Input(Guid = "6e5a4b3c-9d2e-4f3a-8b4c-5d6e7f8a9b1c")]
    public readonly InputSlot<string> CsvPosZMapping = new("Position Z");

    [Input(Guid = "c4e5f6a7-b8c9-4d0e-8f2a-9b8c7d6e5f4d")]
    public readonly InputSlot<string> CsvRotWMapping = new("Rotation W");

    [Input(Guid = "7f4b5c6d-8e3f-4a1b-9c2d-3e4f5a6b7c8d")]
    public readonly InputSlot<string> CsvRotXMapping = new("Rotation X");

    [Input(Guid = "8a3c2d1e-9f4a-4b5c-d6e7-f8a9b0c1d2e3")]
    public readonly InputSlot<string> CsvRotYMapping = new("Rotation Y");

    [Input(Guid = "9b2d3e4f-5a6b-4c7d-8e9f-0a1b2c3d4e5f")]
    public readonly InputSlot<string> CsvRotZMapping = new("Rotation Z");

    [Input(Guid = "a2c1e3d4-f5a6-4b7c-8d9e-0f1a2b3c4d5e")]
    public readonly InputSlot<string> CsvScaleXMapping = new("Scale X");

    [Input(Guid = "b3d4e5f6-a7b8-4c9d-2e1f-0a9b8c7d6e5f")]
    public readonly InputSlot<string> CsvScaleYMapping = new("Scale Y");

    [Input(Guid = "c4e5f6a7-b8c9-4d0e-8f2a-9b8c7d6e5f4c")]
    public readonly InputSlot<string> CsvScaleZMapping = new("Scale Z");

    [Input(Guid = "4d5e6f7a-8b9c-4d1e-9f8a-4b5c6d7e8f9a")]
    public readonly InputSlot<bool> Export = new();

    [Input(Guid = "2b3c4d5e-6f7a-4b1c-9d8e-2f3a4b5c6d7e")]
    public readonly InputSlot<string> ExportFilePath = new("exported_points.json");

    [Input(Guid = "1a2b3c4d-5e6f-4a1b-8c9d-1e2f3a4b5c6d")]
    public readonly InputSlot<string> FilePath = new("points.json");

    [Output(Guid = "5e6f7a8b-9c1d-4e2f-8a3b-5c4d6e7f8a9b")]
    public readonly Slot<BufferWithViews?> PointBuffer = new();

    private bool _convertTriggered, _exportTriggered;
    private List<Point>? _lastConvertedPoints;
    private BufferWithViews? _pointBuffer;

    public DataPointConverter()
    {
        PointBuffer.UpdateAction += Update;
    }

    private async void Update(EvaluationContext context)
    {
        if (MathUtils.WasTriggered(Convert.GetValue(context), ref _convertTriggered))
        {
            Convert.SetTypedInputValue(false);
            await ConvertFileAsync(context);
        }

        if (MathUtils.WasTriggered(Export.GetValue(context), ref _exportTriggered))
        {
            Export.SetTypedInputValue(false);
            await ExportFileAsync(context);
        }

        PointBuffer.Value = _pointBuffer;
    }

    private async Task ConvertFileAsync(EvaluationContext context)
    {
        var filePath = FilePath.GetValue(context);
        if (string.IsNullOrWhiteSpace(filePath))
        {
            Log.Warning("File path is invalid or empty.", this);
            return;
        }

        var resolvedFilePath = Path.IsPathRooted(filePath)
                                   ? filePath
                                   : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, filePath);

        if (!File.Exists(resolvedFilePath))
        {
            Log.Warning($"File does not exist: {resolvedFilePath}", this);
            return;
        }

        Log.Debug($"Starting conversion from '{resolvedFilePath}'...", this);

        try
        {
            var points = Path.GetExtension(resolvedFilePath).ToLowerInvariant() switch
                             {
                                 ".json" => await LoadFromJsonAsync(resolvedFilePath, context),
                                 ".csv"  => await LoadFromCsvAsync(resolvedFilePath, context),
                                 _ => throw new
                                          NotSupportedException($"Unsupported file format '{Path.GetExtension(resolvedFilePath)}'. Please use .csv or .json.")
                             };

            UpdateGpuBufferWithPoints(ref _pointBuffer, points);
            _lastConvertedPoints = points;
            Log.Debug($"Successfully converted {points.Count} points from '{resolvedFilePath}'.", this);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to convert data: {ex.Message}", this);
            UpdateGpuBufferWithPoints(ref _pointBuffer, new List<Point>());
        }
    }

    private async Task ExportFileAsync(EvaluationContext context)
    {
        var filePath = ExportFilePath.GetValue(context);
        if (string.IsNullOrWhiteSpace(filePath))
        {
            Log.Warning("Export file path is empty.", this);
            return;
        }

        if (_lastConvertedPoints == null || _lastConvertedPoints.Count == 0)
        {
            Log.Warning("No points available to export. Please convert data first.", this);
            return;
        }

        var resolvedFilePath = Path.IsPathRooted(filePath)
                                   ? filePath
                                   : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, filePath);

        try
        {
            var ext = Path.GetExtension(resolvedFilePath).ToLowerInvariant();
            Log.Debug($"Starting export to '{resolvedFilePath}'...", this);

            switch (ext)
            {
                case ".json":
                    await SaveToJsonAsync(resolvedFilePath, _lastConvertedPoints);
                    break;
                case ".csv":
                    await SaveToCsvAsync(resolvedFilePath, _lastConvertedPoints);
                    break;
                default:
                    throw new NotSupportedException($"Unsupported export format '{ext}'. Please use .csv or .json.");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to export data: {ex.Message}", this);
        }
    }

    private async Task<List<Point>> LoadFromJsonAsync(string filePath, EvaluationContext context)
    {
        var jsonString = await File.ReadAllTextAsync(filePath);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var records = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(jsonString, options)
                      ?? new List<Dictionary<string, JsonElement>>();

        var mapping = GetColumnMapping(context);
        var points = new List<Point>(records.Count);
        foreach (var record in records)
            points.Add(ParseJsonRecord(record, mapping));

        return points;
    }

    private async Task<List<Point>> LoadFromCsvAsync(string filePath, EvaluationContext context)
    {
        var lines = await File.ReadAllLinesAsync(filePath);
        if (lines.Length <= 1)
        {
            Log.Warning("CSV file is empty or contains only a header.", this);
            return new List<Point>();
        }

        var headers = lines[0].Split(',').Select((h, i) => (Header: h.Trim(), Index: i))
                              .ToDictionary(x => x.Header, x => x.Index, StringComparer.OrdinalIgnoreCase);
        var mapping = GetColumnMapping(context);

        // Create a direct mapping from property name (e.g., "PosX") to column index
        var indexMapping = mapping.ToDictionary(
                                                kvp => kvp.Key,
                                                kvp => headers.TryGetValue(kvp.Value, out var index) ? index : -1
                                               );

        var points = new List<Point>(lines.Length - 1);
        for (var i = 1; i < lines.Length; i++)
        {
            var values = lines[i].Split(',');
            points.Add(ParseCsvRecord(values, indexMapping));
        }

        return points;
    }

    private async Task SaveToJsonAsync(string filePath, List<Point> points)
    {
        var jsonPoints = points.Select(p => new
                                                {
                                                    Position = new { p.Position.X, p.Position.Y, p.Position.Z },
                                                    Orientation = new { p.Orientation.W, p.Orientation.X, p.Orientation.Y, p.Orientation.Z },
                                                    Scale = new { p.Scale.X, p.Scale.Y, p.Scale.Z },
                                                    p.F1
                                                }).ToList();

        var options = new JsonSerializerOptions { WriteIndented = true };
        var jsonString = JsonSerializer.Serialize(jsonPoints, options);
        await File.WriteAllTextAsync(filePath, jsonString);
        Log.Debug($"Successfully exported {points.Count} points to '{filePath}'.", this);
    }

    private async Task SaveToCsvAsync(string filePath, List<Point> points)
    {
        await using var writer = new StreamWriter(filePath);
        await writer.WriteLineAsync("Position X,Position Y,Position Z,Rotation X,Rotation Y,Rotation Z,F1,Scale X,Scale Y,Scale Z");

        foreach (var p in points)
        {
            var euler = ToEulerAngles(p.Orientation);
            var line = string.Format(CultureInfo.InvariantCulture,
                                     "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9}",
                                     p.Position.X, p.Position.Y, p.Position.Z,
                                     euler.X, euler.Y, euler.Z,
                                     p.F1,
                                     p.Scale.X, p.Scale.Y, p.Scale.Z);
            await writer.WriteLineAsync(line);
        }

        Log.Debug($"Successfully exported {points.Count} points to '{filePath}'.", this);
    }

    private Point ParseJsonRecord(IReadOnlyDictionary<string, JsonElement> record, Dictionary<string, string> mapping)
    {
        // Position
        var posX = GetFloatFromJson(record, mapping.GetValueOrDefault("PosX"));
        var posY = GetFloatFromJson(record, mapping.GetValueOrDefault("PosY"));
        var posZ = GetFloatFromJson(record, mapping.GetValueOrDefault("PosZ"));

        // Rotation
        var rotX = GetFloatFromJson(record, mapping.GetValueOrDefault("RotX"));
        var rotY = GetFloatFromJson(record, mapping.GetValueOrDefault("RotY"));
        var rotZ = GetFloatFromJson(record, mapping.GetValueOrDefault("RotZ"));
        var rotWKey = mapping.GetValueOrDefault("RotW");

        Quaternion orientation;
        var haveQuaternion = !string.IsNullOrEmpty(rotWKey) && record.ContainsKey(rotWKey);
        if (haveQuaternion)
        {
            var rotW = GetFloatFromJson(record, rotWKey);
            orientation = new Quaternion(rotX, rotY, rotZ, rotW);
        }
        else
        {
            orientation = Quaternion.CreateFromYawPitchRoll(rotY, rotX, rotZ);
        }

        // Scale
        var scaleX = GetFloatFromJson(record, mapping.GetValueOrDefault("ScaleX"), 1);
        var scaleY = GetFloatFromJson(record, mapping.GetValueOrDefault("ScaleY"), 1);
        var scaleZ = GetFloatFromJson(record, mapping.GetValueOrDefault("ScaleZ"), 1);

        return new Point
                   {
                       Position = new Vector3(posX, posY, posZ),
                       Orientation = orientation,
                       Scale = new Vector3(scaleX, scaleY, scaleZ),
                       F1 = GetFloatFromJson(record, mapping.GetValueOrDefault("F1"))
                   };
    }

    private Point ParseCsvRecord(string[] values, Dictionary<string, int> indexMapping)
    {
        // Position
        var posX = GetFloatFromCsv(values, indexMapping["PosX"]);
        var posY = GetFloatFromCsv(values, indexMapping["PosY"]);
        var posZ = GetFloatFromCsv(values, indexMapping["PosZ"]);

        // Rotation
        var rotX = GetFloatFromCsv(values, indexMapping["RotX"]);
        var rotY = GetFloatFromCsv(values, indexMapping["RotY"]);
        var rotZ = GetFloatFromCsv(values, indexMapping["RotZ"]);
        var rotWIndex = indexMapping["RotW"];

        Quaternion orientation;
        if (rotWIndex != -1)
        {
            var rotW = GetFloatFromCsv(values, rotWIndex);
            orientation = new Quaternion(rotX, rotY, rotZ, rotW);
        }
        else
        {
            orientation = Quaternion.CreateFromYawPitchRoll(rotY, rotX, rotZ);
        }

        // Scale
        var scaleX = GetFloatFromCsv(values, indexMapping["ScaleX"], 1);
        var scaleY = GetFloatFromCsv(values, indexMapping["ScaleY"], 1);
        var scaleZ = GetFloatFromCsv(values, indexMapping["ScaleZ"], 1);

        return new Point
                   {
                       Position = new Vector3(posX, posY, posZ),
                       Orientation = orientation,
                       Scale = new Vector3(scaleX, scaleY, scaleZ),
                       F1 = GetFloatFromCsv(values, indexMapping["F1"])
                   };
    }

    private static float GetFloatFromJson(IReadOnlyDictionary<string, JsonElement> record, string? key, float defaultValue = 0f)
    {
        if (string.IsNullOrWhiteSpace(key) || !record.TryGetValue(key, out var element))
            return defaultValue;

        if (element.ValueKind == JsonValueKind.Number && element.TryGetSingle(out var num))
            return num;

        var raw = element.GetString();
        return ParseFloatString(raw) ?? defaultValue;
    }

    private static float GetFloatFromCsv(string[] values, int index, float defaultValue = 0f)
    {
        if (index < 0 || index >= values.Length)
            return defaultValue;

        var raw = values[index];
        return ParseFloatString(raw) ?? defaultValue;
    }

    private static float? ParseFloatString(string? raw)
    {
        var cleaned = raw?.Replace("m", string.Empty).Replace("°", string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(cleaned))
            return null;

        return float.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private Dictionary<string, string> GetColumnMapping(EvaluationContext context)
    {
        return new Dictionary<string, string>
                   {
                       { "PosX", CsvPosXMapping.GetValue(context) ?? "PosX" },
                       { "PosY", CsvPosYMapping.GetValue(context) ?? "PosY" },
                       { "PosZ", CsvPosZMapping.GetValue(context) ?? "PosZ" },
                       { "RotX", CsvRotXMapping.GetValue(context) ?? "RotX" },
                       { "RotY", CsvRotYMapping.GetValue(context) ?? "RotY" },
                       { "RotZ", CsvRotZMapping.GetValue(context) ?? "RotZ" },
                       { "RotW", CsvRotWMapping.GetValue(context) ?? "RotW" },
                       { "F1", CsvF1Mapping.GetValue(context) ?? "F1"},
                       { "ScaleX", CsvScaleXMapping.GetValue(context) ?? "ScaleX" },
                       { "ScaleY", CsvScaleYMapping.GetValue(context) ?? "ScaleY" },
                       { "ScaleZ", CsvScaleZMapping.GetValue(context) ?? "ScaleZ" }
                   };
    }

    private static Vector3 ToEulerAngles(Quaternion q)
    {
        // ... implementation remains the same ...
        Vector3 angles = new();
        double sinrCosp = 2 * (q.W * q.X + q.Y * q.Z);
        double cosrCosp = 1 - 2 * (q.X * q.X + q.Y * q.Y);
        angles.X = (float)Math.Atan2(sinrCosp, cosrCosp);

        double sinp = 2 * (q.W * q.Y - q.Z * q.X);
        if (Math.Abs(sinp) >= 1)
            angles.Y = (float)Math.CopySign(Math.PI / 2, sinp);
        else
            angles.Y = (float)Math.Asin(sinp);

        double sinyCosp = 2 * (q.W * q.Z + q.X * q.Y);
        double cosyCosp = 1 - 2 * (q.Y * q.Y + q.Z * q.Z);
        angles.Z = (float)Math.Atan2(sinyCosp, cosyCosp);
        return angles;
    }

    private void UpdateGpuBufferWithPoints(ref BufferWithViews? buffer, List<Point> list)
    {
        var count = list.Count;
        var currentCount = buffer?.Buffer != null ? buffer.Buffer.Description.SizeInBytes / Point.Stride : 0;

        if (count != currentCount)
        {
            buffer?.Dispose();
            buffer = null;
        }

        if (count == 0) return;

        var array = list.ToArray();
        if (buffer == null)
        {
            buffer = new BufferWithViews();
            ResourceManager.SetupStructuredBuffer(array, Point.Stride * count, Point.Stride, ref buffer.Buffer);
            ResourceManager.CreateStructuredBufferSrv(buffer.Buffer, ref buffer.Srv);
            ResourceManager.CreateStructuredBufferUav(buffer.Buffer, UnorderedAccessViewBufferFlags.None, ref buffer.Uav);
        }
        else
        {
            ResourceManager.Device.ImmediateContext.UpdateSubresource(array, buffer.Buffer);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _pointBuffer?.Dispose();

        base.Dispose(disposing);
    }
}