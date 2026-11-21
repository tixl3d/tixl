#nullable enable
using System.Threading;

namespace Lib.io.video;

/// <summary>
/// One worker per <see cref="VideoDeviceInput"/> instance owns ALL camera-control I/O — the evaluation
/// thread never performs a get/set/enumerate. The eval thread only enqueues jobs (applies, coalesced per
/// (device, control) by replace-on-enqueue; refreshes, debounced; handle invalidations) and reads the
/// published results: an immutable <see cref="CameraControlCatalog"/> snapshot, the catalog text and the
/// last control status message — each a volatile swap, the same publication idiom the capture thread uses
/// for its status message. No lock is shared with the capture thread; the device is only ever touched
/// through the backend's own separate handle (a second fd / a separate COM instance), exactly the property
/// that made the old eval-thread control traffic safe next to the capture loop.
///
/// Apply ordering: an apply resolves its control name against the catalog snapshot FOR THAT DEVICE. A
/// missing or stale snapshot defers the apply — coalesced per (device, control) like pending applies —
/// until that device's EnumerateCatalog completes (then: name found → apply, not found → the standard
/// unsupported warning); completing a refresh for device X drops pending applies aimed at devices that
/// are neither X nor the newest refresh request — a stale apply for an abandoned device can never
/// resolve against a newer device's snapshot, while applies for a just-selected device survive the
/// previous device's still-running enumeration (the switch force-applies them in the same frame) and
/// resolve when the new device's own refresh completes. Dispose drops whatever remains.
/// </summary>
internal sealed class CameraControlWorker : IDisposable
{
    /// <summary>Catalog text / status for platforms where no control backend is registered.</summary>
    public const string DeviceControlsUnavailableText = "Device controls are not available on this platform.";

    private const int JobWaitTimeoutMs = 250;
    private static readonly TimeSpan JoinTimeout = TimeSpan.FromSeconds(2);

    private readonly ICameraControlBackend? _backend;

    // Eval thread writes these through the Enqueue methods; the worker thread clears/reads them. A plain
    // stale read can only cause a missed debounce (one extra refresh), never a lost job.
    private volatile int _pendingRefreshIndex = -1;
    // The device of the newest EnqueueRefresh, never cleared: the worker's drop-on-refresh-completion
    // keeps applies for this device even when the completing refresh is for an older one — a device
    // switch force-applies values for the new device while the old device's enumeration may still run.
    private volatile int _lastRequestedRefreshIndex = -1;

    // Replace-on-enqueue IS the coalescing: a ConcurrentQueue supports neither removal nor replacement,
    // and last-write-wins matches how the device ends up anyway when an animated Pan input fires at 60 Hz.
    private readonly ConcurrentDictionary<(int DeviceIndex, string ControlName), PendingApply> _pendingApplies = new();

    // One-shot jobs (handle invalidation, refresh). Drained invalidation-first, then the newest refresh.
    private readonly ConcurrentQueue<WorkerJob> _jobs = new();
    private readonly AutoResetEvent _wake = new(false);

    // Applies whose target device has no snapshot yet — resolved (or dropped on device switch) when that
    // device's enumeration completes. Only the worker thread touches the list.
    private readonly List<PendingApply> _deferredApplies = [];
    private readonly Lock _deferredLock = new();

    private readonly Lock _lifecycleLock = new();
    private Thread? _thread;
    private bool _disposed;

    // Threads whose join timed out in Dispose — the reference keeps the intent inspectable while the
    // orphaned thread observes the shutdown flag, drops its own handles and exits (StopCaptureThread's
    // orphan treatment). IsBackground keeps process exit safe either way.
    private readonly List<Thread> _orphanedThreads = [];

    // Published to the eval thread by volatile swap; immutable once visible.
    private volatile CameraControlCatalog? _currentCatalog;
    private volatile string _catalogText = "";
    private volatile string _lastControlMessage = "";
    private volatile bool _shutdown;

    // Device indices the backend was ever used with — only the worker thread touches the set; used to
    // drop cached handles when the thread winds down.
    private readonly HashSet<int> _touchedDeviceIndices = [];

    public CameraControlWorker()
    {
        // First registered backend whose platform check passes; null on platforms without one — every
        // path below then degrades to honest "not available" messaging and the thread never starts.
        _backend = CameraControlBackendRegistry.Create();
        if (_backend == null)
            _catalogText = DeviceControlsUnavailableText;
    }

    /// <summary>The immutable catalog snapshot for the device of the last completed refresh; null until then.</summary>
    public CameraControlCatalog? CurrentCatalog => _currentCatalog;

    /// <summary>Human-readable catalog text, built by the worker at refresh. Not status-classified — never
    /// carries Warning/Error prefixes.</summary>
    public string CatalogText => _catalogText;

    /// <summary>Last control set/read result for the Status output. Follows the IStatusProvider prefix
    /// contract: failures start with "Warning: " (or "Error: "), informational text carries no prefix.</summary>
    public string LastControlMessage => _lastControlMessage;

    /// <summary>On device select / Reconnect / first sight of a device. Debounced: a refresh for a device
    /// already pending (queued, not yet running) is skipped. A request arriving while the SAME device's
    /// refresh is running is queued too — it must be able to win over a refresh queued for another device
    /// in between (user flips A→B→A inside A's enumeration window), or the worker's final published
    /// catalog would be for the wrong device and the flip-back would be lost until the next switch.</summary>
    public void EnqueueRefresh(WebcamWithIndex device)
    {
        if (_backend == null)
        {
            _catalogText = DeviceControlsUnavailableText;
            return;
        }

        var index = device.Index;
        if (_pendingRefreshIndex == index)
            return;

        _pendingRefreshIndex = index;
        _lastRequestedRefreshIndex = index;
        if (!TryEnqueueJob(new WorkerJob(WorkerJobKind.Refresh, device)))
            _pendingRefreshIndex = -1;
    }

    /// <summary>
    /// Applies a value and/or an auto mode to one control. Coalesced per (device, control): a newer apply
    /// replaces an older pending one. <paramref name="auto"/> is the strict two-way switch — true engages
    /// the control's auto mode (value ignored); false means the apply lands as Manual; null leaves the auto
    /// mode to the shared value rule (a value apply is always Manual and disengages the linked auto).
    /// </summary>
    public void EnqueueApply(WebcamWithIndex device, string controlName, int? value, bool? auto)
    {
        if (_backend == null)
        {
            // Nothing will ever resolve this apply — say so instead of parking it forever.
            _lastControlMessage = "Warning: " + DeviceControlsUnavailableText;
            return;
        }

        lock (_lifecycleLock)
        {
            if (_disposed)
                return;

            StartThreadLocked();
            _pendingApplies[(device.Index, controlName)] = new PendingApply(device, controlName, value, auto);
            _wake.Set();
        }
    }

    /// <summary>Drops the backend's cached handle for the device (fd / COM filter) so the next job re-opens
    /// it — called at the Reconnect site and on device switch, where a camera replaced at the same index
    /// would otherwise be driven through a stale handle.</summary>
    public void InvalidateHandles(WebcamWithIndex device)
    {
        if (_backend == null)
            return;

        TryEnqueueJob(new WorkerJob(WorkerJobKind.InvalidateHandles, device));
    }

    public void Dispose()
    {
        lock (_lifecycleLock)
        {
            if (_disposed)
                return;
            _disposed = true;
        }

        _shutdown = true;
        _wake.Set();

        var thread = _thread;
        _thread = null;
        if (thread == null)
            return;

        // Mirrors StopCaptureThread: a DirectShow Get/GetRange into a wedged or just-unplugged filter can
        // block far beyond the join, so on timeout the thread is orphaned — it observes the shutdown flag
        // on its next loop iteration, closes its own handles and exits.
        if (!thread.Join(JoinTimeout))
        {
            Log.Debug("Camera control worker did not exit in time; leaving it to finish on its own.");
            lock (_lifecycleLock)
            {
                _orphanedThreads.Add(thread);
            }
        }

        // The wake event is intentionally never disposed: an orphaned thread may still be waiting on it,
        // and the SafeWaitHandle finalizer reclaims the handle.
    }

    private bool TryEnqueueJob(WorkerJob job)
    {
        lock (_lifecycleLock)
        {
            if (_disposed)
                return false;

            StartThreadLocked();
            _jobs.Enqueue(job);
            _wake.Set();
            return true;
        }
    }

    private void StartThreadLocked()
    {
        if (_thread != null)
            return;

        _thread = new Thread(ThreadLoop)
                  {
                      IsBackground = true,
                      Name = "Video Device Controls",
                      Priority = ThreadPriority.BelowNormal
                  };
        _thread.Start();
    }

    private void ThreadLoop()
    {
        // All COM objects (DirectShow filters/interfaces) are created and released on this thread, in this
        // apartment. The declaration compiles on every platform; the call runs on Windows only.
        var isWindows = OperatingSystem.IsWindows();
        if (isWindows)
            _ = CoInitializeEx(IntPtr.Zero, CoInitMultithreaded);

        try
        {
            while (!_shutdown)
            {
                // Timeout, not infinite wait, so a shutdown while idle is observed promptly.
                _wake.WaitOne(JobWaitTimeoutMs);
                if (_shutdown)
                    break;

                ProcessJobs();
            }
        }
        catch (Exception e)
        {
            // The worker must never take the editor down from here; a dead worker only means stale
            // catalogs and silent applies until the op is recreated.
            Log.Debug($"Camera control worker crashed: {e}");
        }
        finally
        {
            var backend = _backend;
            foreach (var index in _touchedDeviceIndices)
                backend?.InvalidateHandles(new WebcamWithIndex("", index));

            if (isWindows)
                CoUninitialize();
        }
    }

    private void ProcessJobs()
    {
        // Drain the one-shot queue: invalidations first (a dropped handle must not serve this round's
        // refresh or applies), then the refresh — only the newest one matters.
        WorkerJob? refreshJob = null;
        while (_jobs.TryDequeue(out var job))
        {
            _touchedDeviceIndices.Add(job.Device.Index);
            if (job.Kind == WorkerJobKind.InvalidateHandles)
                _backend?.InvalidateHandles(job.Device);
            else
                refreshJob = job;
        }

        if (refreshJob is { } refresh)
            RunRefresh(refresh.Device);

        ProcessPendingApplies();
    }

    private void RunRefresh(WebcamWithIndex device)
    {
        // Clear the debounce marker — unless a newer refresh for another device was enqueued meanwhile.
        Interlocked.CompareExchange(ref _pendingRefreshIndex, -1, device.Index);

        var catalog = _backend?.EnumerateCatalog(device);
        if (catalog == null)
        {
            // Open failed / device gone / nothing readable. Applies for THIS device stay deferred and
            // are dropped by the next device switch or Dispose — the status below is the user-visible
            // warning, so they are not resolved against an empty snapshot one warning each.
            var current = _currentCatalog;
            if (current == null || current.Device.Index == device.Index)
                _currentCatalog = null;
            _catalogText = $"No device controls found for '{device.Name}'.";
            SetControlMessage($"Warning: No device controls found for '{device.Name}'.");
            DropAppliesForOtherDevices(device.Index, warn: false);
            return;
        }

        // Initial read pass: current value + auto state per control. A control that fails to read
        // stays unread (shown as "—" in the catalog text) instead of failing the whole refresh.
        var controls = new List<CameraControlDescriptor>(catalog.Controls.Count);
        foreach (var control in catalog.Controls)
        {
            var withState = _backend!.TryGetValue(device, control, out var value, out var isAuto)
                                ? control with { CurrentValue = value, CurrentIsAuto = isAuto }
                                : control;
            controls.Add(withState);
        }

        var snapshot = new CameraControlCatalog(device, DateTime.Now, controls);
        _currentCatalog = snapshot;
        _catalogText = BuildCatalogText(snapshot);

        // The device-switch drop trigger and the deferred-apply resolution fire here: the snapshot
        // for THIS device now exists.
        CompleteRefresh(device, snapshot);
    }

    private void CompleteRefresh(WebcamWithIndex device, CameraControlCatalog catalog)
    {
        // Applies aimed at devices the user has left can never resolve against this snapshot — dropped
        // with a warning before the resolution below so a resolved apply's status wins if both happen.
        // Applies for the newest refresh request are KEPT: a device switch force-applies values for the
        // new device in the same frame that enqueues its refresh, so those applies can already be
        // pending/deferred while the PREVIOUS device's (possibly seconds-long) enumeration completes
        // here. They resolve when the new device's own refresh completes.
        DropAppliesForOtherDevices(device.Index, warn: true);

        List<PendingApply> resolved;
        lock (_deferredLock)
        {
            if (_deferredApplies.Count == 0)
                return;
            resolved = _deferredApplies.Where(a => a.Device.Index == device.Index).ToList();
            if (resolved.Count == 0)
                return;
            _deferredApplies.RemoveAll(a => a.Device.Index == device.Index);
        }

        foreach (var apply in resolved)
            ExecuteApply(apply, catalog);
    }

    private void DropAppliesForOtherDevices(int deviceIndex, bool warn)
    {
        // The completing device's applies are kept by the caller's contract; applies for the newest
        // refresh request are kept too — that refresh is queued or running and will resolve them.
        var lastRequested = _lastRequestedRefreshIndex;
        var droppedDevices = new HashSet<string>();
        foreach (var key in _pendingApplies.Keys.ToArray())
        {
            if (key.DeviceIndex == deviceIndex || key.DeviceIndex == lastRequested)
                continue;
            if (_pendingApplies.TryRemove(key, out var dropped))
                droppedDevices.Add(dropped.Device.Name);
        }

        lock (_deferredLock)
        {
            for (var i = _deferredApplies.Count - 1; i >= 0; i--)
            {
                var index = _deferredApplies[i].Device.Index;
                if (index == deviceIndex || index == lastRequested)
                    continue;

                droppedDevices.Add(_deferredApplies[i].Device.Name);
                _deferredApplies.RemoveAt(i);
            }
        }

        if (warn && droppedDevices.Count > 0)
            SetControlMessage($"Warning: Pending control changes for {string.Join(", ", droppedDevices)} "
                              + "were dropped — the device changed.");
    }

    private void ProcessPendingApplies()
    {
        foreach (var key in _pendingApplies.Keys.ToArray())
        {
            if (!_pendingApplies.TryRemove(key, out var apply))
                continue;

            _touchedDeviceIndices.Add(apply.Device.Index);

            var catalog = _currentCatalog;
            if (catalog == null || catalog.Device.Index != apply.Device.Index)
            {
                // Missing or stale snapshot — defer, never resolve against the wrong device's catalog.
                // Same replace-on-enqueue coalescing as _pendingApplies: a newer deferred apply
                // replaces an older one for the same (device, control), so an animated input deferring
                // across many worker rounds leaves ONE entry, and a recovery burst writes each control
                // once with its newest value instead of replaying the whole backlog.
                lock (_deferredLock)
                {
                    _deferredApplies.RemoveAll(a => a.Device.Index == apply.Device.Index
                                                    && string.Equals(a.ControlName, apply.ControlName, StringComparison.Ordinal));
                    _deferredApplies.Add(apply);
                }
                continue;
            }

            ExecuteApply(apply, catalog);
        }
    }

    private void ExecuteApply(PendingApply apply, CameraControlCatalog catalog)
    {
        var backend = _backend;
        if (backend == null)
            return;

        var descriptor = catalog.Find(apply.ControlName);
        if (descriptor == null)
        {
            SetControlMessage($"Warning: Camera does not support {apply.ControlName}.");
            return;
        }

        // Strict two-way auto switch: engaging auto ignores the value entirely.
        if (apply.Auto == true)
        {
            if (backend.TrySetAuto(apply.Device, descriptor, true))
            {
                SetControlMessage($"{descriptor.Name} set to automatic.");
                RepublishAfterApply(apply.Device, descriptor.Name);
            }
            else
            {
                SetControlMessage($"Warning: Failed to set {descriptor.Name} to automatic.");
            }

            return;
        }

        if (apply.Value is { } requested)
        {
            // Shared apply rule: the write always lands as Manual — the backend disengages the control's
            // linked auto first (DirectShow implicitly via Set(Manual), Linux via an explicit partner
            // write, macOS via the mode set). Without it, a value write against an auto-grayed (INACTIVE)
            // control silently no-ops — the factory state of most UVC webcams.
            if (backend.TrySetValue(apply.Device, descriptor, requested))
            {
                if (descriptor.Kind == CameraControlKind.Button)
                {
                    // Buttons are trigger-style; their read-back is skipped.
                    SetControlMessage($"{descriptor.Name} triggered.");
                }
                else
                {
                    var clampedNote = "";
                    if (backend.TryGetValue(apply.Device, descriptor, out var actual, out _) && actual != requested)
                        clampedNote = $" (device clamped to {actual})";

                    var estimatedNote = (descriptor.States & CameraControlStates.EstimatedRange) != 0
                                            ? " — device did not report a range"
                                            : "";
                    var autoNote = apply.Auto == false && !string.IsNullOrEmpty(descriptor.AutoControlName)
                                       ? $" Auto ({descriptor.AutoControlName}) disengaged."
                                       : "";
                    SetControlMessage($"{descriptor.Name} set to {requested}{clampedNote}. "
                                      + $"Range {descriptor.Minimum}..{descriptor.Maximum}, step {descriptor.Step}"
                                      + $"{estimatedNote}.{autoNote}");
                }

                RepublishAfterApply(apply.Device, descriptor.Name);
            }
            else
            {
                SetControlMessage($"Warning: Failed to set {descriptor.Name} to {requested}.");
            }

            return;
        }

        if (apply.Auto == false)
        {
            // Manual mode without a value.
            if (backend.TrySetAuto(apply.Device, descriptor, false))
            {
                SetControlMessage($"{descriptor.Name} set to manual mode.");
                RepublishAfterApply(apply.Device, descriptor.Name);
            }
            else
            {
                SetControlMessage($"Warning: Failed to set {descriptor.Name} to manual mode.");
            }
        }
    }

    // After a successful apply: re-query the affected control and its auto partner (one cheap read each)
    // and republish the snapshot — this is what keeps the INACTIVE gray-out live after toggling an auto
    // control, without a full re-enumeration.
    private void RepublishAfterApply(WebcamWithIndex device, string controlName)
    {
        var catalog = _currentCatalog;
        var backend = _backend;
        if (catalog == null || backend == null || catalog.Device.Index != device.Index)
            return;

        List<CameraControlDescriptor>? updated = null;
        for (var i = 0; i < catalog.Controls.Count; i++)
        {
            var control = catalog.Controls[i];
            var affected = string.Equals(control.Name, controlName, StringComparison.OrdinalIgnoreCase)
                           || (!string.IsNullOrEmpty(control.AutoControlName)
                               && string.Equals(control.AutoControlName, controlName, StringComparison.OrdinalIgnoreCase));
            if (!affected)
                continue;

            updated ??= [.. catalog.Controls];
            if (backend.TryGetValue(device, control, out var value, out var isAuto))
                updated[i] = control with { CurrentValue = value, CurrentIsAuto = isAuto };
        }

        if (updated == null)
            return;

        _currentCatalog = new CameraControlCatalog(device, DateTime.Now, updated);
        _catalogText = BuildCatalogText(_currentCatalog);
    }

    private void SetControlMessage(string message)
    {
        _lastControlMessage = message;
    }

    private static string BuildCatalogText(CameraControlCatalog catalog)
    {
        var text = new System.Text.StringBuilder($"Controls of '{catalog.Device.Name}':");
        if (catalog.Controls.Count == 0)
        {
            text.Append("\n  (the device reports no controls)");
            return text.ToString();
        }

        foreach (var control in catalog.Controls)
        {
            var kind = control.Kind switch
                       {
                           CameraControlKind.Integer   => "int",
                           CameraControlKind.Boolean   => "bool",
                           CameraControlKind.Menu      => "menu",
                           CameraControlKind.IntegerMenu => "menu",
                           _                           => "button",
                       };

            text.Append("\n  ");
            text.Append(control.Name.PadRight(28));
            text.Append(kind.PadRight(7));
            text.Append($"{control.Minimum}..{control.Maximum}".PadLeft(14));
            text.Append($"  step {control.Step}".PadRight(12));
            text.Append($"default {control.DefaultValue}".PadRight(16));

            if (control.Kind == CameraControlKind.Button)
                text.Append("= trigger");
            else if (control.CurrentValue is { } value)
                text.Append($"= {value}");
            else
                text.Append("= —");

            var partner = string.IsNullOrEmpty(control.AutoControlName)
                              ? null
                              : catalog.Find(control.AutoControlName);
            if (control.CurrentIsAuto)
                text.Append(" [auto]");

            if (partner is { CurrentIsAuto: true })
                text.Append($"  inactive ({partner.Name} is on)");
            else if ((control.States & CameraControlStates.Inactive) != 0)
                text.Append("  inactive");

            if ((control.States & CameraControlStates.ReadOnly) != 0)
                text.Append(" [read-only]");
            if ((control.States & CameraControlStates.WriteOnly) != 0)
                text.Append(" [write-only]");
            if ((control.States & CameraControlStates.Grabbed) != 0)
                text.Append(" [grabbed]");
            if ((control.States & CameraControlStates.EstimatedRange) != 0)
                text.Append(" [estimated range]");

            if (control.Items.Length > 0)
            {
                var items = control.Items.Take(12)
                                    .Select(item => item.Label.Length > 0 ? $"{item.Value} {item.Label}" : item.Value.ToString());
                text.Append($"   items: {string.Join(" | ", items)}");
                if (control.Items.Length > 12)
                    text.Append(" | …");
            }
        }

        return text.ToString();
    }

    // COINIT.MULTITHREADED — the worker creates and releases all COM objects on its own thread.
    private const int CoInitMultithreaded = 0x0;

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr reserved, int coInit);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();

    private sealed record WorkerJob(WorkerJobKind Kind, WebcamWithIndex Device);

    private enum WorkerJobKind
    {
        InvalidateHandles,
        Refresh,
    }

    private sealed record PendingApply(WebcamWithIndex Device, string ControlName, int? Value, bool? Auto);
}
