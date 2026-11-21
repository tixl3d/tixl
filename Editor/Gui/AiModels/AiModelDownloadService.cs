#nullable enable
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using T3.Core.Logging;
using T3.Core.Settings;

namespace T3.Editor.Gui.AiModels;

/// <summary>
/// Downloads catalog models into the model store, reporting progress the settings window and the setup
/// wizard can show. One download per catalog entry runs at a time; the browser rows, the one-click setup
/// and the wizard share this service. Finished downloads stay listed until the model is removed.
/// <para>
/// Each file streams into a <c>.part</c> file and is moved into place only after its length matched the
/// server's Content-Length, so a file with its final name is always complete. A dropped connection is
/// retried and resumed with a range request; a connection that stops delivering data is treated as
/// dropped after <see cref="StallTimeout"/>, so a hung download can always be cancelled.
/// </para>
/// </summary>
internal static class AiModelDownloadService
{
    internal enum States { NotStarted, Downloading, Completed, Failed, Cancelled }

    internal sealed class DownloadStatus
    {
        public volatile States State = States.NotStarted;
        public float Progress;          // 0..1 across all files of the entry
        public string CurrentFile = ""; // name and size of the file being downloaded
        public string? Error;

        public long BytesDone;
        public long BytesTotal;

        /** Smoothed download rate, 0 until the first measurement. */
        public double BytesPerSecond;
    }

    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(45);
    private const int MaxAttemptsPerFile = 4;

    private static readonly object StartLock = new();
    private static readonly ConcurrentDictionary<string, DownloadStatus> StatusByEntryId = new();
    private static readonly ConcurrentDictionary<string, Task> RunningTasks = new();
    private static readonly ConcurrentDictionary<string, CancellationTokenSource> CancellationByEntryId = new();

    private static readonly DownloadStatus NotStarted = new();

    // No overall timeout: multi-GB files legitimately take long. Stalls are caught per read instead.
    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient(new SocketsHttpHandler
                                        {
                                            AllowAutoRedirect = true,
                                            MaxAutomaticRedirections = 10,
                                            ConnectTimeout = TimeSpan.FromSeconds(30),
                                        })
                         {
                             Timeout = Timeout.InfiniteTimeSpan,
                         };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("TiXL", "1.0"));
        return client;
    }

    public static DownloadStatus GetStatus(ModelCatalogEntry entry) =>
        StatusByEntryId.TryGetValue(entry.Id, out var status) ? status : NotStarted;

    public static bool IsRunning(ModelCatalogEntry entry) => RunningTasks.ContainsKey(entry.Id);

    /// <summary>Starts downloading all missing files of the entry in the background; false when it was already running.</summary>
    public static bool Start(ModelCatalogEntry entry)
    {
        lock (StartLock)
        {
            if (RunningTasks.ContainsKey(entry.Id))
                return false;

            StartLocked(entry);
            return true;
        }
    }

    /** Returns the entry's running download task, starting one when none is running. */
    public static Task GetOrStartTask(ModelCatalogEntry entry)
    {
        lock (StartLock)
        {
            if (RunningTasks.TryGetValue(entry.Id, out var running))
                return running;

            return StartLocked(entry);
        }
    }

    /// <summary>
    /// Stops the entry's running download, also while the connection is stalled. Partial files are
    /// removed, already completed files stay. A cancelled download can be started again.
    /// </summary>
    public static void Cancel(ModelCatalogEntry entry)
    {
        if (CancellationByEntryId.TryGetValue(entry.Id, out var cancellation))
        {
            try
            {
                cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The download finished in the meantime
            }
        }
    }

    private static Task StartLocked(ModelCatalogEntry entry)
    {
        var status = StatusByEntryId.GetOrAdd(entry.Id, _ => new DownloadStatus());
        status.State = States.Downloading;
        status.Error = null;
        status.Progress = 0;
        status.CurrentFile = "";
        status.BytesDone = 0;
        status.BytesTotal = 0;
        status.BytesPerSecond = 0;

        var cancellation = new CancellationTokenSource();
        CancellationByEntryId[entry.Id] = cancellation;

        Task task = null!;
        task = Task.Run(async () =>
        {
            try
            {
                await DownloadEntryAsync(entry, status, cancellation.Token);
            }
            finally
            {
                CancellationByEntryId.TryRemove(entry.Id, out _);
                cancellation.Dispose();

                // Only remove our own registration; a newer download of the same entry may have started
                if (RunningTasks.TryGetValue(entry.Id, out var current) && current == task)
                    RunningTasks.TryRemove(entry.Id, out _);
            }
        });

        RunningTasks[entry.Id] = task;
        return task;
    }

    /** Removes the entry's files from the store and forgets its last download result. */
    public static void Remove(ModelCatalogEntry entry)
    {
        if (IsRunning(entry))
            return;

        if (entry.RequiresLocalExport)
            TryDeleteDirectory(entry.ExportFolder);
        else
        {
            foreach (var file in entry.Files)
            {
                TryDeleteFile(entry.TargetPath(file.TargetFileName));
                TryDeleteFile(entry.TargetPath(file.TargetFileName) + ".part");
            }
        }

        StatusByEntryId.TryRemove(entry.Id, out _);
        CleanupEmptyFolders(entry);
    }

    private static void CleanupEmptyFolders(ModelCatalogEntry entry)
    {
        try
        {
            var familyDirectory = AiModelSettings.FamilyDirectory(entry.Family.StoreFolder());

            if (!string.IsNullOrEmpty(entry.InstallSubFolder))
            {
                var subFolder = Path.Combine(familyDirectory, entry.InstallSubFolder);
                if (Directory.Exists(subFolder) && Directory.GetFileSystemEntries(subFolder).Length == 0)
                    Directory.Delete(subFolder);
            }

            if (Directory.Exists(familyDirectory) && Directory.GetFileSystemEntries(familyDirectory).Length == 0)
                Directory.Delete(familyDirectory);
        }
        catch
        {
            // Not being able to tidy up an empty folder is not worth an error
        }
    }

    private static async Task DownloadEntryAsync(ModelCatalogEntry entry, DownloadStatus status, CancellationToken cancellation)
    {
        try
        {
            cancellation.ThrowIfCancellationRequested();

            if (!AiModelSettings.EnsureFamilyDirectory(entry.Family.StoreFolder(), out var familyDirectory, out var createError))
                throw new IOException(createError);

            if (!string.IsNullOrEmpty(entry.InstallSubFolder))
                Directory.CreateDirectory(Path.Combine(familyDirectory, entry.InstallSubFolder));

            // The total is known up front, so the bar moves evenly across all files of the entry
            var totalBytes = 0L;
            var completedBytes = 0L;
            foreach (var file in entry.Files)
            {
                totalBytes += file.ExpectedSizeBytes;
                if (File.Exists(entry.TargetPath(file.TargetFileName)))
                    completedBytes += file.ExpectedSizeBytes;
            }

            status.BytesTotal = totalBytes;
            status.BytesDone = completedBytes;
            UpdateProgress(status);

            var meter = new RateMeter();
            foreach (var file in entry.Files)
            {
                cancellation.ThrowIfCancellationRequested();

                // A file with its final name was verified when it was moved into place
                var targetPath = entry.TargetPath(file.TargetFileName);
                if (File.Exists(targetPath))
                    continue;

                var actualSize = await DownloadFileWithRetriesAsync(file, targetPath, status, completedBytes, meter, cancellation);

                // Correct the total when the source's size differs from the catalog estimate
                status.BytesTotal += actualSize - file.ExpectedSizeBytes;
                completedBytes += actualSize;
                status.BytesDone = completedBytes;
                UpdateProgress(status);
            }

            status.Progress = 1;
            status.State = States.Completed;
            status.CurrentFile = "";
            Log.Info($"[AI Models] Installed '{entry.DisplayName}' into the model store");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            DeletePartialFiles(entry);
            status.State = States.Cancelled;
            status.CurrentFile = "";
            Log.Info($"[AI Models] Download of '{entry.DisplayName}' cancelled");
        }
        catch (Exception e)
        {
            status.Error = DescribeError(e);
            status.State = States.Failed;
            status.CurrentFile = "";
            Log.Error($"[AI Models] Download of '{entry.DisplayName}' failed: {status.Error}");
        }
    }

    private static async Task<long> DownloadFileWithRetriesAsync(CatalogFile file, string targetPath, DownloadStatus status,
                                                                 long completedBytes, RateMeter meter, CancellationToken cancellation)
    {
        for (var attempt = 1;; attempt++)
        {
            try
            {
                return await DownloadFileAsync(file, targetPath, status, completedBytes, meter, cancellation);
            }
            catch (Exception e) when (attempt < MaxAttemptsPerFile
                                      && !cancellation.IsCancellationRequested
                                      && IsTransient(e))
            {
                Log.Warning($"[AI Models] {file.TargetFileName}: {DescribeError(e)} - retrying ({attempt}/{MaxAttemptsPerFile - 1})");
                status.CurrentFile = $"{file.TargetFileName}  (connection lost, retrying…)";
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt), cancellation);
            }
        }
    }

    /// <summary>
    /// Streams one file into a .part staging file, resuming a previous partial download when the server
    /// supports range requests, then moves it into place once its length is verified.
    /// </summary>
    private static async Task<long> DownloadFileAsync(CatalogFile file, string targetPath, DownloadStatus status,
                                                      long completedBytes, RateMeter meter, CancellationToken cancellation)
    {
        var partPath = targetPath + ".part";
        var resumeFrom = File.Exists(partPath) ? new FileInfo(partPath).Length : 0L;

        using var request = new HttpRequestMessage(HttpMethod.Get, file.Url);
        if (resumeFrom > 0)
            request.Headers.Range = new RangeHeaderValue(resumeFrom, null);

        using var response = await SendWithStallTimeoutAsync(request, cancellation);

        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            // The partial file is stale or already complete with a changed source: start over
            TryDeleteFile(partPath);
            throw new TransientDownloadException("The partial download did not match the source; restarting.");
        }

        response.EnsureSuccessStatusCode();

        var isResumed = resumeFrom > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (!isResumed)
            resumeFrom = 0;

        // Full length of the file at the source, when the server tells us
        long? expectedLength = response.Content.Headers.ContentRange?.Length
                               ?? (response.Content.Headers.ContentLength is { } contentLength ? contentLength + resumeFrom : null);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellation);
        var downloaded = resumeFrom;

        await using (var output = new FileStream(partPath, isResumed ? FileMode.Append : FileMode.Create,
                                                 FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
        {
            var buffer = new byte[1 << 16];
            while (true)
            {
                var read = await ReadWithStallTimeoutAsync(stream, buffer, cancellation);
                if (read == 0)
                    break;

                await output.WriteAsync(buffer.AsMemory(0, read), cancellation);
                downloaded += read;

                status.BytesDone = completedBytes + downloaded;
                status.BytesPerSecond = meter.Add(read);
                UpdateProgress(status);

                var sizeText = expectedLength is > 0
                                   ? $"{AiModelCatalog.FormatSize(downloaded)} / {AiModelCatalog.FormatSize(expectedLength.Value)}"
                                   : AiModelCatalog.FormatSize(downloaded);
                status.CurrentFile = $"{file.TargetFileName}  ({sizeText})";
            }
        }

        if (expectedLength is > 0 && downloaded != expectedLength.Value)
        {
            // Truncated transfer: keep the part file, the retry resumes it
            throw new TransientDownloadException(
                $"{file.TargetFileName} ended after {AiModelCatalog.FormatSize(downloaded)} of {AiModelCatalog.FormatSize(expectedLength.Value)}.");
        }

        // Without a Content-Length only a wildly wrong size (e.g. an error page) is rejected
        if (expectedLength is null && file.ExpectedSizeBytes > 0 && downloaded < file.ExpectedSizeBytes / 2)
        {
            TryDeleteFile(partPath);
            throw new IOException($"{file.TargetFileName} arrived with {AiModelCatalog.FormatSize(downloaded)}, expected about " +
                                  $"{AiModelCatalog.FormatSize(file.ExpectedSizeBytes)} - the source may have changed. Try again later.");
        }

        TryDeleteFile(targetPath);
        File.Move(partPath, targetPath);
        return downloaded;
    }

    private static async Task<HttpResponseMessage> SendWithStallTimeoutAsync(HttpRequestMessage request, CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(StallTimeout);
        try
        {
            return await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            throw new TransientDownloadException($"The server did not answer within {StallTimeout.TotalSeconds:0} s.");
        }
    }

    private static async Task<int> ReadWithStallTimeoutAsync(Stream stream, byte[] buffer, CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(StallTimeout);
        try
        {
            return await stream.ReadAsync(buffer.AsMemory(), timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            throw new TransientDownloadException($"No data received for {StallTimeout.TotalSeconds:0} s.");
        }
    }

    private static void UpdateProgress(DownloadStatus status)
    {
        var total = status.BytesTotal;
        status.Progress = total > 0 ? Math.Clamp(status.BytesDone / (float)total, 0f, 1f) : 0f;
    }

    private static bool IsTransient(Exception e) => e switch
        {
            TransientDownloadException => true,
            HttpRequestException { StatusCode: null } => true, // connection-level failure
            HttpRequestException { StatusCode: { } code } => (int)code >= 500 || code == HttpStatusCode.TooManyRequests,
            IOException => true, // includes socket resets while reading
            _ => false,
        };

    private static string DescribeError(Exception e) => e switch
        {
            HttpRequestException { StatusCode: HttpStatusCode.NotFound } => "The model is no longer available at its source (404).",
            HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden } =>
                "The source refused the download (access denied). It may require a login now.",
            HttpRequestException { StatusCode: { } code } => $"The server answered {(int)code} {code}.",
            HttpRequestException h => $"Could not connect: {h.InnerException?.Message ?? h.Message}",
            UnauthorizedAccessException => "No permission to write to the model store. Choose another store folder in Settings > AI Models.",
            IOException io when io.HResult is unchecked((int)0x80070070) or unchecked((int)0x80070027) => "The disk is full.",
            _ => e.Message,
        };

    /** Removes the .part staging files after a cancelled download; finished files stay. */
    private static void DeletePartialFiles(ModelCatalogEntry entry)
    {
        foreach (var file in entry.Files)
            TryDeleteFile(entry.TargetPath(file.TargetFileName) + ".part");
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception e)
        {
            Log.Warning($"[AI Models] Could not delete {path}: {e.Message}");
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception e)
        {
            Log.Warning($"[AI Models] Could not delete {path}: {e.Message}");
        }
    }

    private sealed class TransientDownloadException(string message) : IOException(message);

    /** Download rate smoothed over about a second, for the speed and remaining-time readout. */
    private sealed class RateMeter
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private long _bytesSinceSample;
        private double _lastSampleSeconds;
        private double _rate;

        public double Add(long bytes)
        {
            _bytesSinceSample += bytes;
            var now = _clock.Elapsed.TotalSeconds;
            var dt = now - _lastSampleSeconds;
            if (dt < 0.25)
                return _rate;

            var instant = _bytesSinceSample / dt;
            _rate = _rate <= 0 ? instant : _rate * 0.7 + instant * 0.3;
            _bytesSinceSample = 0;
            _lastSampleSeconds = now;
            return _rate;
        }
    }
}
