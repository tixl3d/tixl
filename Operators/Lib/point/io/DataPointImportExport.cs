#nullable enable
using System.Globalization;
using System.Text.Json;
using SharpDX;
using SharpDX.Direct3D11;
using T3.Core.Utils;

namespace Lib.point.io;

/// <summary>
///     Imports a JSON point list into a GPU structured buffer and optionally exports it again.
///     The node keeps the imported data in an internal buffer so the points stay visible even
///     when the import button is released.
/// </summary>
[Guid("d4e5f6a7-b8c9-4d0e-1f2a-9b8c7d6e5f4a")]
public sealed class DataPointImportExport : Instance<DataPointImportExport>
{
    public DataPointImportExport()
    {
        // Use the subscription pattern – do not overwrite any existing callbacks.
        PointBufferOut.UpdateAction += Update;
    }

    #region Update ----------------------------------------------------------
    private async void Update(EvaluationContext context)
    {
        var bufferIn = PointBufferIn.GetValue(context);

        var currentImportPath = ImportFilePath.GetValue(context);

        // Autoload logic: Check if the path changed and is not null/whitespace, 
        // and it's not the path we just successfully imported.
        var pathChangedAndValid = !string.IsNullOrWhiteSpace(currentImportPath)
                                  && !currentImportPath.Equals(_lastImportedFilePath, StringComparison.Ordinal);

        // ------------------- IMPORT ------------------ -
        var explicitImportTriggered = MathUtils.WasTriggered(Import.GetValue(context), ref _importTriggered);

        if (explicitImportTriggered)
        {
            // Reset the button after we have detected the trigger.
            Import.SetTypedInputValue(false);
            await ImportFileAsync(context);
        }
        else if (pathChangedAndValid)
        {
            // Autoload triggered by file path change
            await ImportFileAsync(context);
        }

        // ------------------- EXPORT ------------------ -
        if (MathUtils.WasTriggered(Export.GetValue(context), ref _exportTriggered))
        {
            // Reset the button after we have detected the trigger.
            Export.SetTypedInputValue(false);
            await ExportFileAsync(context, bufferIn);
        }

        // Keep the internal buffer alive.  If we have imported data we always expose it,
        // otherwise we forward the incoming buffer (if any) downstream.
        PointBufferOut.Value = _pointBuffer ?? bufferIn;
    }
    #endregion

    #region Import ------------------------------------------------------------
    private async Task ImportFileAsync(EvaluationContext context)
    {
        var importPath = ImportFilePath.GetValue(context);
        if (string.IsNullOrWhiteSpace(importPath))
        {
            Log.Warning("Import file path is invalid.", this);
            return;
        }

        var resolvedPath = Path.IsPathRooted(importPath)
                               ? importPath
                               : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, importPath);

        if (!File.Exists(resolvedPath))
        {
            Log.Warning($"Import file does not exist: {resolvedPath}", this);
            // Do not update _lastImportedFilePath on non-existence to allow autoload retry when the path is corrected.
            return;
        }

        try
        {
            var points = await LoadFromJsonAsync(resolvedPath);
            UpdateGpuBufferWithPoints(ref _pointBuffer, points);
            _lastImportedPoints = points;
            _lastImportedFilePath = importPath; // Update tracking path on success
            Log.Debug($"Successfully imported {points.Count} points from '{resolvedPath}'.", this);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to import data: {ex.Message}", this);
            // Do not update _lastImportedFilePath on failure.
        }
    }
    #endregion

    #region Export ------------------------------------------------------------
    private async Task ExportFileAsync(EvaluationContext context, BufferWithViews? bufferIn)
    {
        var exportPath = ExportFilePath.GetValue(context);
        if (string.IsNullOrWhiteSpace(exportPath))
        {
            Log.Warning("Export file path is empty.", this);
            return;
        }

        var resolvedPath = Path.IsPathRooted(exportPath)
                               ? exportPath
                               : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, exportPath);

        // Prefer the incoming buffer if the user has connected one,
        // otherwise fall back to the data we imported internally.
        var pointsToExport = bufferIn != null ? ToPointList(bufferIn) : _lastImportedPoints;

        if (pointsToExport == null || pointsToExport.Count == 0)
        {
            Log.Warning("No points available to export. Please import data or connect a point buffer.", this);
            return;
        }

        try
        {
            await SaveToJsonAsync(resolvedPath, pointsToExport);
            Log.Debug($"Successfully exported {pointsToExport.Count} points to '{resolvedPath}'.", this);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to export data: {ex.Message}", this);
        }
    }
    #endregion

    #region GPU Buffer ----------------------------------------------------------
    private void UpdateGpuBufferWithPoints(ref BufferWithViews? buffer, List<Point>? list)
    {
        var count = list?.Count ?? 0;
        var currentCount = buffer?.Buffer != null
                               ? buffer.Buffer.Description.SizeInBytes / Point.Stride
                               : 0;

        // If the element count changed we must recreate the structured buffer.
        if (count != currentCount)
        {
            buffer?.Dispose();
            buffer = null;
        }

        if (count == 0)
            return;

        var array = list?.ToArray();

        if (buffer == null)
        {
            buffer = new BufferWithViews();
            if (array != null)
                ResourceManager.SetupStructuredBuffer(array,
                                                      Point.Stride * count,
                                                      Point.Stride,
                                                      ref buffer.Buffer);
            ResourceManager.CreateStructuredBufferSrv(buffer.Buffer, ref buffer.Srv);
            ResourceManager.CreateStructuredBufferUav(buffer.Buffer,
                                                      UnorderedAccessViewBufferFlags.None,
                                                      ref buffer.Uav);
        }
        else
        {
            // Simple sub‑resource update – fast path.
            ResourceManager.Device.ImmediateContext.UpdateSubresource(array, buffer.Buffer);
        }

        Log.Debug($"GPU buffer updated with {count} points.", this);
    }
    #endregion

    #region Buffer → Point List (CPU read‑back) ----------------------------------
    private List<Point> ToPointList(BufferWithViews? bufferWithViews)
    {
        if (bufferWithViews?.Buffer == null)
            return new List<Point>();

        var device = ResourceManager.Device;
        var context = device.ImmediateContext;
        var desc = bufferWithViews.Buffer.Description;
        var pointCount = desc.SizeInBytes / Point.Stride;

        // Build a staging buffer that the CPU can read.
        var stagingDesc = new BufferDescription
                              {
                                  SizeInBytes = desc.SizeInBytes,
                                  Usage = ResourceUsage.Staging,
                                  BindFlags = BindFlags.None,
                                  CpuAccessFlags = CpuAccessFlags.Read,
                                  OptionFlags = ResourceOptionFlags.BufferStructured,
                                  StructureByteStride = desc.StructureByteStride
                              };

        using var staging = new Buffer(device, stagingDesc);
        context.CopyResource(bufferWithViews.Buffer, staging);

        var dataBox = new DataBox();
        try
        {
            dataBox = context.MapSubresource(staging, 0,
                                             MapMode.Read,
                                             MapFlags.None);
            using var stream = new DataStream(dataBox.DataPointer,
                                              dataBox.RowPitch,
                                              true,
                                              false);
            var points = stream.ReadRange<Point>(pointCount);
            return points.ToList();
        }
        catch (Exception ex)
        {
            Log.Error($"Error reading GPU buffer: {ex.Message}", this);
            return new List<Point>();
        }
        finally
        {
            if (dataBox.DataPointer != IntPtr.Zero)
                context.UnmapSubresource(staging, 0);
        }
    }
    #endregion

    #region Disposal -------------------------------------------------------------
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _pointBuffer?.Dispose();

        base.Dispose(disposing);
    }
    #endregion

    #region JSON IO --------------------------------------------------------------
    private async Task<List<Point>> LoadFromJsonAsync(string filePath)
    {
        var jsonString = await File.ReadAllTextAsync(filePath);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var records = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(jsonString, options)
                      ?? new List<Dictionary<string, JsonElement>>();

        Log.Debug($"Found {records.Count} records in JSON file.", this);
        var points = new List<Point>(records.Count);
        foreach (var rec in records)
            points.Add(ParseRecord(rec));

        return points;
    }

    private async Task SaveToJsonAsync(string filePath, List<Point> points)
    {
        var jsonPoints = points.Select(p => new
                                                {
                                                    Position = new { p.Position.X, p.Position.Y, p.Position.Z },
                                                    Orientation = new
                                                                      {
                                                                          p.Orientation.W,
                                                                          p.Orientation.X,
                                                                          p.Orientation.Y,
                                                                          p.Orientation.Z
                                                                      },
                                                    Scale = new { p.Scale.X, p.Scale.Y, p.Scale.Z },
                                                    p.F1
                                                }).ToList();

        var jsonString = JsonSerializer.Serialize(jsonPoints,
                                                  new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(filePath, jsonString);
    }
    #endregion

    #region Record → Point ---------------------------------------------------------
    private Point ParseRecord(IReadOnlyDictionary<string, JsonElement> record)
    {
        // Position
        var posX = GetNestedFloat(record, "Position", "X");
        var posY = GetNestedFloat(record, "Position", "Y");
        var posZ = GetNestedFloat(record, "Position", "Z");

        // Orientation – quaternion (fallback to identity if any component is missing)
        var rotX = GetNestedFloat(record, "Orientation", "X");
        var rotY = GetNestedFloat(record, "Orientation", "Y");
        var rotZ = GetNestedFloat(record, "Orientation", "Z");
        var rotW = GetNestedFloat(record, "Orientation", "W", 1f); // default W = 1 (unit quaternion)

        // Scale (default = 1)
        var scaleX = GetNestedFloat(record, "Scale", "X", 1);
        var scaleY = GetNestedFloat(record, "Scale", "Y", 1);
        var scaleZ = GetNestedFloat(record, "Scale", "Z", 1);

        // Additional custom float (optional)
        var f1 = GetFloat(record, "F1");

        return new Point
                   {
                       Position = new Vector3(posX, posY, posZ),
                       Orientation = new Quaternion(rotX, rotY, rotZ, rotW),
                       Scale = new Vector3(scaleX, scaleY, scaleZ),
                       F1 = f1
                   };
    }

    private static float GetFloat(IReadOnlyDictionary<string, JsonElement> record, string key, float defaultValue = 0f)
    {
        if (!record.TryGetValue(key, out var element))
            return defaultValue;

        if (element.ValueKind == JsonValueKind.Number && element.TryGetSingle(out var num))
            return num;

        return ParseFloatString(element.GetString()) ?? defaultValue;
    }

    private static float GetNestedFloat(IReadOnlyDictionary<string, JsonElement> record,
                                        string parentKey,
                                        string childKey,
                                        float defaultValue = 0f)
    {
        if (!record.TryGetValue(parentKey, out var parent) || parent.ValueKind != JsonValueKind.Object)
            return defaultValue;

        if (!parent.TryGetProperty(childKey, out var child))
            return defaultValue;

        if (child.ValueKind == JsonValueKind.Number && child.TryGetSingle(out var num))
            return num;

        return ParseFloatString(child.GetString()) ?? defaultValue;
    }

    private static float? ParseFloatString(string? raw)
    {
        var cleaned = raw?.Replace("m", string.Empty).Replace("°", string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(cleaned))
            return null;

        return float.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }
    #endregion

    #region Fields & Slots -------------------------------------------------------
    private bool _importTriggered, _exportTriggered;
    private BufferWithViews? _pointBuffer; // internal GPU buffer that holds the imported data
    private List<Point>? _lastImportedPoints; // cached point list for export when no input buffer is connected
    private string? _lastImportedFilePath; // track the last path we successfully imported from (for autoload)

    // ---------- INPUTS ----------
    [Input(Guid = "27320742-221f-4383-8241-995083c87b8b")]
    public readonly InputSlot<BufferWithViews> PointBufferIn = new();

    [Input(Guid = "f8d41a2a-4c27-4a52-a0a2-1304cb04b80f")]
    public readonly InputSlot<string> ImportFilePath = new("points.json");

    [Input(Guid = "3e5b7c7d-8a9f-4b1e-9c2d-3e4f5a6b7c8d")]
    public readonly InputSlot<string> ExportFilePath = new("points.json");

    [Input(Guid = "a6b7c8d9-e0f1-4a2b-8c3d-4e5f6a7b8c9d")]
    public readonly InputSlot<bool> Import = new();

    [Input(Guid = "b7c8d9e0-f1a2-4b3c-9d4e-5f6a7b8c9d0e")]
    public readonly InputSlot<bool> Export = new();

    // ---------- OUTPUT ----------
    [Output(Guid = "c8d9e0f1-a2b3-4c4d-8e5f-6a7b8c9d0e1f")]
    public readonly Slot<BufferWithViews?> PointBufferOut = new();
    #endregion
}