#nullable enable
using T3.Graphics.Compat;

namespace Lib.Utils;

/// <summary>
/// Uploads structured lists into one reused GPU structured buffer with views, for [UploadToGpu] and the ops that
/// output GPU points directly. The lists are concatenated and must share their element type.
/// </summary>
internal sealed class StructuredListUploader
{
    public int ElementCount { get; private set; }

    /// <returns>The uploaded buffer, or null when the lists are empty or the upload failed.</returns>
    public BufferWithViews? Upload(List<StructuredList> lists, Instance owner)
    {
        ElementCount = 0;
        var totalSizeInBytes = 0;
        var elementSizeInBytes = 0;
        foreach (var list in lists)
        {
            totalSizeInBytes += list.TotalSizeInBytes;
            if (elementSizeInBytes == 0)
                elementSizeInBytes = list.ElementSizeInBytes;
        }

        // Empty lists give no buffer, like missing ones; a wrapper without a buffer is invalid downstream
        if (totalSizeInBytes == 0 || elementSizeInBytes == 0)
            return null;

        using (var data = new DataStream(totalSizeInBytes, true, true))
        {
            foreach (var list in lists)
                list.WriteToStream(data);

            data.Position = 0;
            try
            {
                ResourceManager.SetupStructuredBuffer(data, totalSizeInBytes, elementSizeInBytes, ref _buffer);
            }
            catch (Exception e)
            {
                Log.Error("Failed to setup structured buffer " + e.Message, owner);
                return null;
            }
        }

        ResourceManager.CreateStructuredBufferSrv(_buffer, ref _bufferWithViews.Srv);
        ResourceManager.CreateStructuredBufferUav(_buffer, UnorderedAccessViewBufferFlags.None, ref _bufferWithViews.Uav);
        _bufferWithViews.Buffer = _buffer;
        ElementCount = totalSizeInBytes / elementSizeInBytes;
        return _bufferWithViews;
    }

    private Buffer? _buffer;
    private readonly BufferWithViews _bufferWithViews = new();
}
