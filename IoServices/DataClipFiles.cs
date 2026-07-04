#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;
using T3.Core.DataTypes.DataSet;

namespace T3.IoServices;

/// <summary>
/// Resolves any file that can serve as a DataClip source (<c>.data</c> recordings, MIDI
/// files) to a shared, cached <see cref="DataSet"/>. Central extension dispatch so UI code
/// showing clip content doesn't hard-code per-format rules.
/// </summary>
public static class DataClipFiles
{
    public static bool TryGetDataSetForFile(string path, string absolutePath, [NotNullWhen(true)] out DataSet? dataSet, out string failureReason)
    {
        if (path.EndsWith(".data", StringComparison.OrdinalIgnoreCase))
        {
            return DataSetCache.TryGet(absolutePath, out dataSet, out failureReason);
        }

        if (path.EndsWith(".mid", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".midi", StringComparison.OrdinalIgnoreCase))
        {
            return MidiFileToDataSet.TryGet(absolutePath, out dataSet, out failureReason);
        }

        dataSet = null;
        failureReason = $"Not a data-clip file format: {path}";
        return false;
    }
}
