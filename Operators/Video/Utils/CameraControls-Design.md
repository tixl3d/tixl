# Design — Device-Driven Camera Controls for `[VideoDeviceInput]`

The Windows DirectShow *Video Capture Filter Properties* dialog, as a data-driven operator surface:
every webcam exposes **every control it actually offers** — with the range, step, default and
auto/manual state each device reports — on Windows (DirectShow), Linux (V4L2) and macOS (AVFoundation).

The design replaces the fixed 16-value `CameraControl` enum backend in `CameraControls.cs` with:

- a small **shared descriptor model** (one control: kind, range, default, auto link, menu items),
- a **backend interface** (`enumerate catalog / get / set / set-auto`) plus a registry that dispatches
  by `OperatingSystem.Is*()` to one file per OS,
- a **control worker thread** so all control I/O runs off the eval thread, publishing immutable
  snapshots the op reads,
- **op integration** that keeps the six existing patchable inputs working exactly as before and adds
  a generic name + value + apply surface plus a readable `ControlsCatalog` output.

Everything platform-specific below was verified during design: the Linux claims against the real
cameras on this machine with a Python ioctl probe (a Logitech C922 Pro at `/dev/video0`, a DJI Pocket 3
at `/dev/video2`, kernel 6.12), the DirectShow claims by reflecting `DirectShowLib.Standard 2.1.0`
(the exact package `Operators/Video/Video.csproj:122` references), the macOS claims against Apple's
AVFoundation documentation.

---

## 1. Verified platform facts (what this design is grounded in)

### 1.1 Linux / V4L2 — probe: `python3` ioctl scripts against `/dev/video0` and `/dev/video2`

Structs and ioctl numbers from `/usr/include/linux/videodev2.h` and `/usr/include/linux/v4l2-controls.h`
(mainline UAPI, kernel 6.12):

- `struct v4l2_queryctrl` = **68 bytes** (videodev2.h:1892-1902: u32 id, u32 type, u8 name[32],
  s32 min/max/step/default, u32 flags, u32 reserved[2]).
- `VIDIOC_QUERYCTRL` = `_IOWR('V', 36, struct v4l2_queryctrl)` (videodev2.h:2644),
  `VIDIOC_QUERYMENU` = `_IOWR('V', 37, struct v4l2_querymenu)` (:2645),
  `VIDIOC_G_CTRL`/`VIDIOC_S_CTRL` = nr 27/28 over `struct v4l2_control` = 8 bytes (:2638-2639).
- `struct v4l2_querymenu` = **44 bytes, packed** (:1922-1930: u32 id, u32 index,
  union { u8 name[32]; s64 value; }, u32 reserved).
- Control types (:1841-1849): 1 INTEGER, 2 BOOLEAN, 3 MENU, 4 BUTTON, 5 INTEGER64,
  **6 CTRL_CLASS**, 7 STRING, 8 BITMASK, 9 INTEGER_MENU.
- Flags (:1933-1948): DISABLED 0x001, GRABBED 0x002, READ_ONLY 0x004, UPDATE 0x008, INACTIVE 0x010,
  SLIDER 0x020, WRITE_ONLY 0x040, VOLATILE 0x080, NEXT_CTRL 0x80000000.
- Control classes (v4l2-controls.h:19-21): USER `0x00980000`, CAMERA `0x009a0000`;
  `V4L2_CID_BASE = 0x00980900`, `V4L2_CID_CAMERA_CLASS_BASE = 0x009A0900`.

**Live result 1 — the size-field bug in today's code is real.** `CameraControls.cs:127` builds
`Iowr(36, 64)`; the kernel demands 68. Verified on both cameras:

```
/dev/video0: QUERYCTRL size=64 → errno 25 (ENOTTY)   size=68 → OK  name="Brightness"
/dev/video2: QUERYCTRL size=64 → errno 25 (ENOTTY)   size=68 → OK/EINVAL per control (see below)
```

The rejection errno for the wrong size is incidental, not part of the contract: this machine's probe
returns ENOTTY (25) for size 64 on both cameras (re-run and re-confirmed during this design pass),
while an independent review run on the same machine measured EINVAL (22) on `/dev/video2` — the
kernel can reject a bad `_IOWR` size in either the ioctl dispatch or the driver handler, so which
errno appears is not stable. Nothing downstream keys on it; the operative, fully-reproducing claim is
that size 64 is rejected and size 68 is correct.

So today `IsSupportedLinux` never gets a device-reported range and silently degrades to the
G_CTRL-probe + conventional-range fallback (`CameraControls.cs:205-219`).

**Live result 2 — the NEXT_CTRL walk has an off-by-one trap.** The kernel returns the first control
with id **strictly greater** than the input id. The canonical loop must re-seed with **the returned id
itself** (| NEXT_CTRL), *not* returned+1. Verified on the C922:

```
seed with returned+1 → 14 entries (silently drops every control whose id == previous id + 1:
                        Contrast 0x00980901, Sharpness 0x0098091B, Exposure-Absolute 0x009A0902,
                        Tilt 0x009A0909, Zoom 0x009A090D)
seed with returned   → 19 entries (complete)
```

**Live result 3 — the corrected catalog of the C922** (walk + QUERYMENU, sizes 68/44):

```
0x00980001 User Controls              CTRL_CLASS   (flags READ_ONLY|WRITE_ONLY)  ← skip: class marker
0x00980900 Brightness                 INT       0..255     step 1   def 128
0x00980901 Contrast                   INT       0..255     step 1   def 128
0x00980902 Saturation                 INT       0..255     step 1   def 128
0x0098090C White Balance, Automatic   BOOL      0..1       step 1   def 1
0x00980913 Gain                       INT       0..255     step 1   def 0
0x00980918 Power Line Frequency       MENU      0..2       step 1   def 2   [0:Disabled, 1:50 Hz, 2:60 Hz]
0x0098091A White Balance Temperature  INT    2000..6500    step 1   def 4000   flags INACTIVE  ← WB auto is on
0x0098091B Sharpness                  INT       0..255     step 1   def 128
0x0098091C Backlight Compensation     INT       0..1       step 1   def 0
0x009A0001 Camera Controls            CTRL_CLASS  (flags READ_ONLY|WRITE_ONLY) ← skip: class marker
0x009A0901 Auto Exposure              MENU      0..3       step 1   def 3   [no items: uvcvideo returns -EINVAL]
0x009A0902 Exposure Time, Absolute    INT       3..2047    step 1   def 250   flags INACTIVE  ← auto exposure
0x009A0903 Exposure, Dynamic Framerate BOOL     0..1       step 1   def 0
0x009A0908 Pan, Absolute              INT  -36000..36000  step 3600 def 0
0x009A0909 Tilt, Absolute             INT  -36000..36000  step 3600 def 0
0x009A090A Focus, Absolute            INT       0..250     step 5   def 0   flags INACTIVE  ← focus auto is on
0x009A090C Focus, Automatic Continuous BOOL    0..1       step 1   def 1
0x009A090D Zoom, Absolute             INT     100..500     step 1   def 100
```

The three INACTIVE flags are the V4L2 counterpart of the DirectShow dialog graying a slider while its
Auto checkbox is ticked — the catalog must carry them. The empty `Auto Exposure` menu shows menu
items can be absent even for a MENU control (kernel returns `-EINVAL` for QUERYMENU) — the surface
must fall back to showing raw indices.

**Live result 4 — DJI Pocket 3 (`/dev/video2`)**: complete walk = 4 entries: the camera class marker
plus Pan (`-126000..774000` step 3600), Tilt, Zoom (`100..400` step 1). Direct
`QUERYCTRL(0x00980900)` returns EINVAL even with size 68 — the device has no user-class controls at
all. Two consequences: device-reported ranges are wildly different from any conventional table (the
DJI Pan is 12× the UVC fallback), and the walk works even on devices where per-id queries of absent
controls fail.

**Live result 5 — 8 of the 16 CIDs hardcoded in `CameraControls.cs:133-151` are wrong**, verified by
querying each hardcoded id directly on the C922 and against the UAPI headers:

| Input today | Hardcoded | Actually is (header) | On C922 | Correct CID |
|---|---|---|---|---|
| Roll | 0x009A0912 | `IRIS_RELATIVE` (no Roll CID exists in mainline) | EINVAL (absent) | — none on Linux |
| Iris | 0x009A0910 | `PRIVACY` | EINVAL | `IRIS_ABSOLUTE` 0x009A0911 |
| Exposure | 0x009A0901 | `EXPOSURE_AUTO` — a **menu**; the op writes integers into it | MENU "Auto Exposure" | `EXPOSURE_ABSOLUTE` 0x009A0902 |
| Gamma | 0x00980908 | `AUDIO_TREBLE` | EINVAL | `GAMMA` 0x00980910 |
| Sharpness | 0x00980911 | `EXPOSURE` (user class) | EINVAL | `SHARPNESS` 0x0098091B |
| WhiteBalance | 0x00980910 | `GAMMA` | EINVAL | `WB_TEMPERATURE` 0x0098091A |
| BacklightCompensation | 0x00980912 | `AUTOGAIN` | EINVAL | `BACKLIGHT_COMPENSATION` 0x0098091C |
| Gain | 0x0098090B | `BLACK_LEVEL` (deprecated) | EINVAL | `GAIN` 0x00980913 |

Correct today and confirmed live: Pan `0x009A0908`, Tilt `0x009A0909`, Zoom `0x009A090D`,
Focus `0x009A090A`, Brightness `0x00980900`, Contrast `0x00980901`, Saturation `0x00980902`
(Hue `0x00980903` is the correct CID but this device doesn't offer Hue). The auto controls the fixed
set never mapped — `AUTO_WHITE_BALANCE 0x0098090C`, `FOCUS_AUTO 0x009A090C`, `EXPOSURE_AUTO
0x009A0901` — are all live-present on the C922.

### 1.2 Windows / DirectShow — reflection of `DirectShowLib.Standard 2.1.0`

Reflected from the exact DLL in the local NuGet cache (`dotnet run` console probe referencing
`directshowlib.standard/2.1.0/lib/netstandard2.0/DirectShowLib.dll`):

```
enum VideoProcAmpProperty:  Brightness=0 Contrast=1 Hue=2 Saturation=3 Sharpness=4 Gamma=5
                            ColorEnable=6 WhiteBalance=7 BacklightCompensation=8 Gain=9
enum CameraControlProperty: Pan=0 Tilt=1 Roll=2 Zoom=3 Exposure=4 Iris=5 Focus=6
enum VideoProcAmpFlags:     None=0 Auto=1 Manual=2
enum CameraControlFlags:    None=0 Auto=1 Manual=2
IAMVideoProcAmp:  GetRange(prop, out min, out max, out step, out default, out flags)
                  Get(prop, out value, out flags)   Set(prop, value, flags)
IAMCameraControl: (same shape)
```

Notes for the design:

- **`ColorEnable` (6) is in the fixed Video Proc Amp catalog but has no mapping in today's code**
  (`CameraControls.cs:412-427` maps 9 of 10). The dialog the ask references includes it.
- `Get`'s flags out-parameter carries the live **Auto/Manual state per property** — today discarded
  (`CameraControls.cs:343,346`), while `Set` hardcodes `Manual` (:369,372). That out-parameter is the
  auto checkbox; the design uses it.
- Unsupported properties surface as `E_PROP_ID_UNSUPPORTED` (DirectShowLib raises COMException) —
  today handled by the blanket `catch` (:323-326); enumeration turns that into "omit from catalog".
- Reach stays as today: `DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice)` monikers bound to
  `IBaseFilter` per call (`CameraControls.cs:386-395`) — index identity = moniker order, the same
  order `WebcamScan.cs:279-285` caches. No new DirectShow access (no property-page dialog, no
  MediaFoundation) is introduced.

### 1.3 macOS / AVFoundation — Apple documentation, honest scope

Verified selector/property surface of `AVCaptureDevice` (Apple docs; see Sources):

- Zoom: `videoZoomFactor` (CGFloat, clamped to `[minAvailableVideoZoomFactor, maxAvailableVideoZoomFactor]`),
  settable under `lockForConfiguration:`/`unlockForConfiguration`.
- Exposure: modes `AVCaptureExposureMode` { locked=0, autoExpose=1, continuousAutoExposure=2,
  custom=3 }; custom duration/ISO via `setExposureModeCustomWithDuration:ISO:completionHandler:`
  clamped to the active format's `minExposureDuration`/`maxExposureDuration` and `minISO`/`maxISO`.
- Focus: `focusMode` (`AVCaptureFocusMode` locked=0, autoFocus=1, continuousAutoFocus=2),
  `isFocusModeSupported:`.
- White balance: `whiteBalanceMode` (`AVCaptureWhiteBalanceMode` locked=0, auto=1, continuous=2),
  `isWhiteBalanceModeSupported:`; manual gains via
  `setWhiteBalanceModeLockedWithDeviceWhiteBalanceGains:completionHandler:`, each gain in
  `[1.0, maxWhiteBalanceGain]`.
- **No pan, tilt, roll, iris, sharpness, gamma, backlight, gain, powerline** — AVFoundation does not
  expose them. The macOS catalog honestly omits everything absent instead of faking entries.

Interop follows the proven pattern of `MacCameraPermission.cs` (`NativeLibrary.TryLoad` of the
framework at its absolute path, `objc_getClass`, `sel_registerName`, `objc_msgSend`, `CFString`
helpers). Device identity is by `localizedName`, the same string the avfoundation demuxer prefix-
matches (`VideoDeviceInput.cs:594-600`) and `WebcamScan.ScanMacCameras` lists. Camera-permission
gating reuses `MacCameraPermission.IsGrantedOrUnknown()`.

---

## 2. Shared model (`CameraControls.cs`, rewritten)

The one enum-based surface collapses into a data-driven descriptor. Everything here is immutable
value data; backends produce it, the op consumes it.

```csharp
public enum CameraControlKind { Integer, Boolean, Menu, IntegerMenu, Button }

[Flags]
public enum CameraControlStates
{
    None = 0,
    ReadOnly  = 1,   // device reports read-only (V4L2 READ_ONLY) — UI shows value, no writes
    WriteOnly = 2,   // V4L2 WRITE_ONLY — trigger-style, no readable value
    Inactive  = 4,   // V4L2 INACTIVE: a linked auto control is engaged (slider grayed in DS dialog)
    Volatile  = 8,   // V4L2 VOLATILE: value changes out-of-band; never cache reads
    Grabbed   = 16,  // V4L2 GRABBED: resource busy
    EstimatedRange = 32, // range came from UVC conventions, not the device (today's "estimated" note)
}

public readonly record struct CameraControlItem(int Value, string Label);

public sealed record CameraControlDescriptor
{
    public required string Name;             // canonical, UI-facing ("White Balance Temperature")
    public required CameraControlKind Kind;
    public int Minimum, Maximum, Step, DefaultValue;
    public float Scale = 1f;                 // device-units = physical * Scale (macOS float APIs; see §5.3)
    public CameraControlStates States = CameraControlStates.None;
    public CameraControlItem[] Items = [];   // menu items (Value = query index for MENU, the driver's
                                             // own integer for IntegerMenu); may be empty even for Menu
    public string AutoControlName = "";      // canonical name of the paired auto control, "" = none
    public required string BackendId;        // "v4l2:0x0098091A" / "dshow:VideoProcAmp.WhiteBalance" / "avf:videoZoomFactor"
}

public sealed record CameraControlCatalog(
    WebcamWithIndex Device,                  // (Name, Index) — the record WebcamScan already returns
    DateTime CapturedAt,
    IReadOnlyList<CameraControlDescriptor> Controls)
{
    public CameraControlDescriptor? Find(string name);   // ordinal case-insensitive
}
```

`CameraControlKind` covers everything the three backends can report; V4L2 types map 1→Integer,
2→Boolean, 3→Menu, 4→Button, 9→IntegerMenu (videodev2.h:1841-1849); 5 INTEGER64, 6 CTRL_CLASS,
7 STRING, 8 BITMASK and compound (≥0x0100) are skipped. Buttons carry no readable value.
DirectShow maps everything to Integer (Kind Boolean when the reported range is exactly 0..1, which is
how `ColorEnable` and low-end `BacklightCompensation` present).

### Backend interface + registry

```csharp
internal interface ICameraControlBackend
{
    /// <summary>One-shot sweep. Returns null when the device has no controls or failed.</summary>
    CameraControlCatalog? EnumerateCatalog(WebcamWithIndex device);
    bool TryGetValue(WebcamWithIndex device, CameraControlDescriptor control, out int value, out bool isAuto);
    bool TrySetValue(WebcamWithIndex device, CameraControlDescriptor control, int value);
    /// <summary>Engage/leave the control's auto mode (DirectShow flag, V4L2 partner control, AVF mode).</summary>
    bool TrySetAuto(WebcamWithIndex device, CameraControlDescriptor control, bool auto);
}

internal static class CameraControlBackendRegistry
{
    /// <summary>Called by each per-OS file from its [ModuleInitializer].</summary>
    public static void Register(Func<bool> platformCheck, Func<ICameraControlBackend> createBackend);
    /// <summary>First registered backend whose platformCheck() is true; null when none — every caller
    /// must handle null as "this platform has no control backend" (honest empty catalog).</summary>
    public static ICameraControlBackend? Create();
}
```

Dispatch satisfies the `OperatingSystem.Is*()` requirement: each backend registers
`Register(OperatingSystem.IsLinux, () => new V4L2CameraControls())` etc. from a
`[ModuleInitializer]`. Benefits over a compile-time switch here: the shared unit compiles and ships
honest "no backend" behavior on all platforms before any platform unit lands (no broken build between
work units), and a platform file whose native library is missing can simply not register.

The old public API (`CameraControl` enum, `IsSupported`, `TryGetValue`, `TrySetValue`,
`IsRangeEstimated`, `InvalidateCache`, the per-call range cache, the V4L2/DirectShow/macOS method
bodies) is deleted; `VideoDeviceInput` is its only consumer (verified by grep; `WebcamScan.cs:165`
only mentions the file in a comment).

---

## 3. Control worker (`CameraControlWorker.cs`, new)

One worker per `VideoDeviceInput` instance owns **all** control I/O. The eval thread never performs a
get/set/enumerate.

```csharp
internal sealed class CameraControlWorker : IDisposable
{
    // Eval thread — non-blocking, allocation-light:
    public void EnqueueRefresh(WebcamWithIndex device);       // on device select / Reconnect / first sight
    public void EnqueueApply(WebcamWithIndex device, string controlName,
                             int? value, bool? auto);          // coalesced per (device, controlName)
    public void InvalidateHandles(WebcamWithIndex device);     // drop cached fd / COM filter
    public CameraControlCatalog? CurrentCatalog { get; }       // volatile ref — immutable snapshot
    public string? LastControlMessage { get; }                 // volatile — last set/read result text

    // Worker thread — the only caller of ICameraControlBackend:
    //   loop: drain one-shot jobs (invalidations → refresh), then pending applies (deferral rules
    //         below), publish results, wait on the event with a timeout while checking _shutdown
    public void Dispose();   // set _shutdown, signal, join ≤2 s; on timeout orphan like StopCaptureThread
}
```

Contract details:

- **Thread**: `IsBackground = true`, `Name = "Video Device Controls"`, `Priority = BelowNormal`.
  Started lazily on the first enqueue; stopped in `VideoDeviceInput.Dispose(bool)`. Jobs are usually
  short (V4L2 ioctls are microseconds) — but a DirectShow `GetRange`/`Get` into a wedged or
  just-unplugged filter can block far beyond a 2 s join, so there IS an orphan path: on join timeout
  `Dispose` adopts the `StopCaptureThread` treatment (`VideoDeviceInput.cs:392-413`) — the thread
  reference moves to an orphaned list that keeps its state alive while it observes the shutdown flag
  on its next loop iteration and closes its own handles. `IsBackground` keeps process exit safe.
- **Queue**: pending applies live in a
  `ConcurrentDictionary<(int DeviceIndex, string ControlName), PendingApply>` — `EnqueueApply`
  overwrites the entry, which IS the coalescing (a `ConcurrentQueue` supports neither removal nor
  replacement; last-write-wins matches how the device ends up anyway when an animated Pan input
  fires at 60 Hz). One-shot jobs (refresh, handle invalidation) use a small `ConcurrentQueue`; one
  `AutoResetEvent` wakes the worker, which drains invalidations first, then the refresh, then the
  pending applies.
- **Apply-before-catalog ordering**: the worker resolves an apply's control name against the catalog
  snapshot **for that device**. When the snapshot is missing or for a different device — the normal
  case on device switch, where the force-apply (§4.1) enqueues six applies at the same site as the
  refresh, and on the very first device after op start — the applies are **deferred**, not dropped
  and not resolved against the stale snapshot. When `EnumerateCatalog` completes for that device the
  deferred applies resolve against the new catalog (name found → apply; not found → the standard
  unsupported warning); **completing a refresh for device X drops pending applies whose
  `DeviceIndex ≠ X`** — that is the device-switch drop trigger, so a stale apply can never resolve
  against a newer device's snapshot — and `Dispose` drops whatever remains. Deferral
  keeps the force-apply promise ("the configured values are sent to the new camera once") without
  sending anything blind. (A raw-`BackendId` apply path would avoid deferral, but bypasses the
  descriptor's kind/range validation — rejected.)
- **Snapshots**: `CurrentCatalog` is an immutable record published by volatile reference swap —
  readers (eval thread, dropdowns) get a consistent view without locks and may hold an older
  snapshot safely. `LastControlMessage` is a volatile string, the same publication idiom the capture
  thread already uses for `_lastStatusMessage` (`VideoDeviceInput.cs:104,151-154,525-526`).
- **Apply job** = `TrySetAuto`/`TrySetValue` + `TryGetValue` read-back + status text
  (`"Pan set to 3600 (device clamped to 3000). Range -36000..36000, step 3600."` — same shape as
  `VideoDeviceInput.cs:338` today, plus an auto-mode sentence when `auto` was requested). The shared
  value rule: **a value apply always lands as Manual and disengages the control's linked auto
  first** — when the descriptor has an `AutoControlName`, the backend performs its disengage step
  (§5.1/§5.2/§5.3) before `TrySetValue`. Without it, a write against an INACTIVE control silently
  no-ops or fails driver-dependently — and the fixed six, which always apply values, would do
  exactly that on a factory-default UVC camera (WB/Focus/Auto-Exposure engaged). The status
  text follows the `IStatusProvider` prefix contract: failures begin `"Warning: "` (or `"Error: "`
  when the device is gone), informational text carries no prefix — `GetStatusLevel` parses exactly
  those prefixes (`VideoDeviceInput.cs:139-144`) and today's failure path already writes
  `"Warning: Failed to set ..."` (:342). After a
  successful apply the worker re-queries the affected control and its auto partner and republishes
  the catalog — one cheap ioctl/Get each, and it is what keeps INACTIVE gray-out live after toggling
  an auto control.
- **Refresh job** = `EnumerateCatalog` + an initial read pass (current value + isAuto per control)
  + building the `ControlsCatalog` text (§4). Debounced: a refresh for a device already pending or
  running is skipped.
- **Device handles** live only on the worker and are dropped by the `InvalidateHandles` job: Linux
  closes its cached `open()` fd per device index (V4L2 allows several fds per device — the property
  `WebcamScan.cs:231-232` and `CameraControls.cs:256-258` already rely on), Windows releases the
  cached `IBaseFilter` via `Marshal.ReleaseComObject` (the per-call bind at
  `CameraControls.cs:386-395` is correct but chatty for an enumerate+readback sweep). The op calls
  `InvalidateHandles` at the Reconnect site, where `CameraControls.InvalidateCache()` runs today
  (`VideoDeviceInput.cs:195`) — without it, a camera replaced at the same index (routine on Linux,
  where `/dev/videoN` is reused) would leave the worker holding a dead fd or filter, a failure mode
  today's open/close-per-call code cannot have. Handles are also dropped on device switch and
  Dispose. Defense in depth: a backend that hits a persistent error on a cached handle reopens it
  once for that job instead of failing forever. The worker calls `CoInitializeEx(NULL,
  COINIT_MULTITHREADED)` at thread start and `CoUninitialize` at exit, creating and releasing all COM
  objects on its own thread. macOS fetches `AVCaptureDevice` references per job.

**Lock discipline vs the capture loop**: the worker never touches `_lockObject` (frame handoff), the
FFmpeg session, or `_sharedRgba`. Device access stays on separate handles per platform — the exact
property that makes today's eval-thread control traffic safe next to the capture thread
(`CameraControls.cs:256-258`). No new lock is shared with the capture thread; the only synchronization
is the queue/event and the volatile publications. An orphaned capture thread still holding the device
does not block control I/O (separate fd / separate COM instance).

---

## 4. Op integration (`VideoDeviceInput.cs` / `.t3` / `.t3ui`)

### 4.1 The six fixed inputs — unchanged semantics, new plumbing

`_basicConfigs` keeps its six entries (Pan/Tilt/Zoom/Focus/Exposure/Brightness, same input slots,
same GUIDs) but maps to **canonical control names** instead of the `CameraControl` enum. The
NaN-baseline change gating (`_lastAppliedConfigValues`, `VideoDeviceInput.cs:297-327`), the
`GetValue(context)` patchability idiom (:313), and the device-switch force-apply (:213-218) keep
their semantics: what counts as a change, when the camera is touched, and how often — none of that
moves. What changes is the feedback timing, stated plainly: the apply, read-back and status text now
happen on the worker, so success text, clamp notes and unsupported warnings land at least one eval
after the change (a user patching an input and reading Status in the same frame sees the previous
message), and a changed value is enqueued even for a control the device lacks — the unsupported
warning comes from the worker's name resolution instead of the synchronous `IsSupported` gate
(:330-350). A changed value enqueues `worker.EnqueueApply(device, canonicalName, value: rounded,
auto: null)`; the status comes back via `LastControlMessage` on a later eval. An animated input
produces coalesced applies, not one per frame.

Canonical names (the union across backends; the DirectShow property names are the vocabulary):

`Pan, Tilt, Roll, Zoom, Focus, Iris, Exposure, Brightness, Contrast, Hue, Saturation, Sharpness,
Gamma, WhiteBalance, BacklightCompensation, Gain, ColorEnable, PowerLineFrequency` — plus the
Linux-only auto controls, which are first-class catalog entries addressable via `ControlName` and
the targets `AutoControlName` must resolve to (`Catalog.Find`, §2): `WhiteBalanceAuto`, `FocusAuto`,
`ExposureAuto`, `ExposureAutoPriority` (§5.1's normalization table).

The fixed six are a subset. Roll has no V4L2 control (§1.1 result 5) — on Linux the Pan/Tilt inputs
work and Roll simply reports unsupported, exactly as today (worse, actually: today it silently
writes `IRIS_RELATIVE`). And because a value apply is always Manual (§3), dragging any of the six on
a camera whose auto mode is engaged — the factory state on UVC webcams — disengages that auto and
writes the value (Linux: partner write, §5.1) instead of silently no-op-ing against an INACTIVE
control.

Canonical-name assignment per backend: Linux maps by CID from a **corrected table** (§5.1), not by
driver string; Windows maps 1:1 from the property enums; macOS names its AVF-backed controls
(`Zoom`, `Exposure`, `Focus`, `WhiteBalance`) and omits the rest.

### 4.2 New generic surface — any control the device exposes

New inputs (GUIDs fixed here so the .t3/.t3ui/.cs agree):

```csharp
[Input(Guid = "694ce2cc-cbdc-49d2-a591-6ac0d5fa16bd")]
public readonly InputSlot<string> ControlName = new();      // dropdown of catalog names (CustomDropdown)

[Input(Guid = "06c894a8-fe69-4cf6-bf54-b9c155b8f035")]
public readonly InputSlot<float> ControlValue = new();      // device units; for menu controls the
                                                            // selected item's Value (see below)

[Input(Guid = "145615c4-c65b-49e9-abb5-78338579e7a3")]
public readonly InputSlot<bool> ControlAuto = new();        // strict two-way switch: true engages
                                                            // auto (value ignored); false, the
                                                            // default, applies the value as Manual
                                                            // and disengages auto

[Input(Guid = "723a1471-c002-44a5-9b9e-f325d716671f")]
public readonly InputSlot<bool> ApplyControl = new();       // trigger, self-clears (Reconnect idiom)
```

```csharp
[Output(Guid = "60e8db59-6f3a-4891-9232-3ff40c338432")]
public readonly Slot<string> ControlsCatalog = new();       // readable catalog text (Status-shaped)
```

- `ControlName` extends the existing `ICustomDropdownHolder` implementation
  (`VideoDeviceInput.cs:878-950`), which returns one flat `IEnumerable<string>` per input — no
  grouping and no cross-input hint mechanism — so the design stays inside that idiom:
  `GetOptionsForInput` for `ControlName` returns the `CurrentCatalog` names as a flat list in
  catalog order; the `.t3ui` entry carries `"Usage": "CustomDropdown"` like `InputDeviceName` (:157).
- `ControlValue` is a second `CustomDropdown` itself: for a **menu-kind** control its options are
  the selected control's menu items as `"value — label"` strings (resolved from the snapshot by
  `ControlName`; picking one parses the leading value), and for non-menu controls it offers nothing
  and the typed float is used as-is. `ControlValue` carries the menu item's **`CameraControlItem.Value`**,
  not a position: for V4L2 `MENU` (type 3) the backend stores the query index as `Value`, so value ==
  index there, but for `IntegerMenu` (type 9) `Value` is the driver's own integer (the s64
  `QUERYMENU` union value) — a position cannot stand in for it (§2).
- The `ApplyControl` trigger self-clears via `SetTypedInputValue(false)` and enqueues
  `EnqueueApply(device, ControlName, ControlValue, auto: ControlAuto)`. `ControlAuto` is a strict
  two-way switch — the one-sentence contract: **`true` engages the control's auto mode (value
  ignored); `false`, the default, applies the value as Manual, which disengages auto on every
  backend** (the §3 apply rule). There is no "leave auto untouched" option — deliberately, matching
  the DirectShow dialog where moving a slider forces Manual.
- Buttons (`Kind = Button`) apply on trigger; their read-back is skipped (WriteOnly-style).
- `ControlsCatalog` text is built by the worker at refresh, e.g.:

```
Controls of 'Logitech C922 Pro Stream Webcam':
  Brightness                  int     0..255  step 1    default 128   = 128
  Contrast                    int     0..255  step 1    default 128   = 130
  White Balance, Automatic    bool    0..1    step 1    default 1     = 1 [auto]
  White Balance Temperature   int  2000..6500 step 1    default 4000  = 3800  inactive (auto is on)
  Power Line Frequency        menu    0..2    step 1    default 2     = 2   items: 0 Disabled | 1 50 Hz | 2 60 Hz
  Focus, Absolute             int     0..250  step 5    default 0     = —   inactive (Focus, Automatic Continuous is on)
  Pan, Absolute               int -36000..36000 step 3600 default 0     = 0
  ...
```

Both slots are published in `Update` the same way `Status` is (`VideoDeviceInput.cs:283`): read the
volatile worker publications, assign when changed. No slot is ever written from the worker thread.
The catalog text is a standalone output — `GetStatusLevel` classifies only the `Status` slot
(`VideoDeviceInput.cs:139-144`), so catalog lines carry no prefix; `Warning:`/`Error:` prefixes are
the worker status-text contract on `Status` (§3).

### 4.3 Catalog refresh points (never the hot path)

`Update` enqueues `EnqueueRefresh` when: the selected device index changes (:208-219 site), a
`Reconnect` trigger fires — where the worker also receives `InvalidateHandles` at the same site
`CameraControls.InvalidateCache()` is called today (:195) — or the first device is resolved after
start. Refresh is debounced in the worker. There is deliberately **no
per-frame refresh and no hot-plug polling** — Reconnect remains the "device changed" mechanism, as
today.

### 4.4 `OpenSettings`

Stays a stub for now (`VideoDeviceInput.cs:864-869`) with its honest warning; the catalog surface is
the dialog's replacement. (Windows could later restore the native property page via
`ISpecifyPropertyPages` + `OleCreatePropertyFrame` — listed as follow-up, not in scope.)

### 4.5 `.t3` / `.t3ui` additions

`.t3`: defaults `ControlName = ""`, `ControlValue = 0`, `ControlAuto = false`, `ApplyControl = false`.
`.t3ui` InputUis entries for all four inputs (positions continuing the control block: X −200, Y
1425/1500/1575/1650) with descriptions that state device-units semantics, the menu-item-`Value`
convention with `ControlValue`'s CustomDropdown behavior for menus, and the strict two-way
`ControlAuto` contract; `ControlName` and `ControlValue` both carry `"Usage": "CustomDropdown"`.
`ControlsCatalog` OutputUi at (300, 600). The operator Description gains a paragraph about the
generic control surface. No format-version bump — purely additive inputs/outputs.

---

## 5. Per-platform backends (new files, one per OS)

Each file is self-contained: private structs/P-Invokes, the corrected catalog walk or fixed-property
sweep, canonical-name mapping, and the `[ModuleInitializer]` registration.

### 5.1 `CameraControlsV4L2.cs` (Linux)

Request numbers via the existing `Iowr` helper (`CameraControls.cs:124-125`), **corrected sizes**:

```csharp
VidIocQueryCtrl = Iowr(36, 68);   // was 64 → rejected on every control (§1.1 result 1)
VidIocQueryMenu = Iowr(37, 44);   // struct v4l2_querymenu, packed
VidIocGCtrl = Iowr(27, 8); VidIocSCtrl = Iowr(28, 8);   // unchanged, verified correct
```

Enumeration walk (the heart of the unit):

```
id = 0x80000000 (NEXT_CTRL)
loop:
    QUERYCTRL(id) — on EINVAL/errno, stop
    skip returned entry when: type == CTRL_CLASS (6)  [class markers 0x00980001/0x009A0001]
                           or flags & DISABLED
    else map type → Kind (§2), keep min/max/step/default/flags/name
    id = (returned_id & 0x0FFFFFFF) | NEXT_CTRL     ← re-seed with the RETURNED id, never +1
                                                      (+1 silently dropped 5 of 19 controls, §1.1 result 2)
per MENU/INTEGER_MENU entry:
    for index = 0.. while QUERYMENU succeeds:
      MENU → Items[i] = (Value: index, Label: name[32])
      INTEGER_MENU → Items[i] = (Value: (int)s64 union value, Label: value)
    an empty item list is legal (uvcvideo) — surface raw values
```

Canonical-name table (corrected CIDs; live-verified presence markers from §1.1):

| Canonical | CID | Class | C922 |
|---|---|---|---|
| Pan | 0x009A0908 | camera | ✔ |
| Tilt | 0x009A0909 | camera | ✔ |
| Zoom | 0x009A090D | camera | ✔ |
| Focus | 0x009A090A | camera | ✔ (inactive w/ Focus Auto) |
| Iris | 0x009A0911 | camera | ✖ (absent — CID fixed regardless) |
| Exposure | 0x009A0902 | camera | ✔ (inactive w/ Auto Exposure) |
| Brightness | 0x00980900 | user | ✔ |
| Contrast | 0x00980901 | user | ✔ |
| Saturation | 0x00980902 | user | ✔ |
| Hue | 0x00980903 | user | ✖ (absent on this unit) |
| Gamma | 0x00980910 | user | ✖ |
| Sharpness | 0x0098091B | user | ✔ |
| WhiteBalance | 0x0098091A | user | ✔ (inactive w/ WB Auto) |
| BacklightCompensation | 0x0098091C | user | ✔ |
| Gain | 0x00980913 | user | ✔ |

Roll: no mainline CID — Linux never reports it. Mapping a canonical name onto an entry found in the
walk is by CID; walk entries whose CID has no canonical-table entry get a **normalized Name**
(driver label stripped to PascalCase), and that Name — never the raw driver label — is what
`Catalog.Find` and `AutoControlName` match on: "Power Line Frequency" → `PowerLineFrequency`,
"White Balance, Automatic" → `WhiteBalanceAuto`, "Focus, Automatic Continuous" → `FocusAuto`,
"Auto Exposure" → `ExposureAuto`, "Exposure, Dynamic Framerate" → `ExposureAutoPriority`.

Auto links (`AutoControlName` = the normalized canonical Name of the paired auto control, resolved
through `Catalog.Find`, §2), conventional UVC pairs, all live-present on the C922:
WhiteBalance → `WhiteBalanceAuto` (0x0098090C, BOOL — 1 = auto), Focus → `FocusAuto` (0x009A090C,
BOOL — 1 = auto), Exposure → `ExposureAuto` (0x009A0901, MENU with the
`v4l2_exposure_auto_type` values **0 = auto, 1 = manual, 2 = shutter priority, 3 = aperture
priority** — v4l2-controls.h:973-978, re-verified this session). The auto contract for that menu:
**manual is exactly 1; auto is any of {0, 2, 3}; `TrySetAuto(true)` writes 0** — writing 3 engages
Aperture Priority, not Auto, and an isAuto check keyed on 3 misreads a device sitting at plain auto
(0) as manual; the C922's default of 3 masks this on exactly one device. `TryGetValue`'s `isAuto`
reports the partner's state under that contract. INACTIVE flags surface verbatim.

**Write-while-auto rule (explicit on Linux)**: `TrySetValue` on a control whose descriptor carries
an `AutoControlName` disengages the partner first — BOOL partner → write 0; `ExposureAuto` menu →
write 1 (manual) — then `VIDIOC_S_CTRL`s the value. If the disengage write fails, the apply fails
under the §3 status-prefix contract rather than writing blind against an INACTIVE control. DirectShow
gets this implicitly from `Set(prop, value, Manual)`; V4L2 must do it explicitly — the C922 walk
shows Focus-Absolute/WB-Temperature/Exposure-Absolute INACTIVE with the autos on, exactly the state
where a bare value write no-ops.

Get/set: cached fd per device, `VIDIOC_G_CTRL`/`VIDIOC_S_CTRL` (8-byte `struct v4l2_control`,
already proven correct). Sizing note for structs: keep `[StructLayout(Sequential, Pack = 1)]` —
`v4l2_queryctrl` marshals to exactly 68 bytes, `v4l2_querymenu` to 44.

Fallback ladder for devices where QUERYCTRL fails per-id but the control works (the DJI pattern for
user-class controls): the walk records every CID the kernel **returned** — including entries skipped
from the catalog as DISABLED or GRABBED — and after the walk, for each canonical CID the kernel never
returned, probe with `G_CTRL`; if readable, include with the existing conventional ranges
(`CameraControls.cs:261-284`) and flag `EstimatedRange`. Keying on "absent from the kept catalog"
instead would resurrect a driver-disabled control with fabricated conventional ranges. The walk-first
order guarantees device-reported ranges win whenever the driver provides them (the DJI camera-class
controls do report real ranges: §1.1 result 4).

Buttons (`Kind = Button`): apply = `S_CTRL(id, 1)`, no read-back.

### 5.2 `CameraControlsDirectShow.cs` (Windows)

Fixed-catalog sweep, both interfaces, all 17 properties:

```
for prop in VideoProcAmpProperty (0..9, includes ColorEnable) ∪ CameraControlProperty (0..6):
    try GetRange(prop, out min, out max, out step, out def, out _)
    catch COMException (E_PROP_ID_UNSUPPORTED) → omit
    Kind = Integer; Boolean when (min, max, step) == (0, 1, 1)
    Get(prop, out value, out flags) → isAuto = (flags & Auto) != 0   [flag enum: Auto=1, Manual=2]
```

- `TrySetValue` → `Set(prop, value, VideoProcAmpFlags.Manual | CameraControlFlags.Manual)`;
  `TrySetAuto(true)` → `Set(prop, 0, Auto)`; `TrySetAuto(false)` re-applies the last value with
  Manual (DirectShow has no "leave auto" primitive — manual requires a value; use the value from the
  preceding `Get`).
- `ColorEnable` gains its mapping (VideoProcAmpProperty 6) — present in the dialog, missing today.
- COM: worker thread `CoInitializeEx(NULL, COINIT_MULTITHREADED)`; the cached `IBaseFilter` per
  device index is created and released on that thread only (`Marshal.ReleaseComObject` in finally);
  `QueryInterface` casts to `IAMVideoProcAmp`/`IAMCameraControl` (the existing
  `(IAMCameraControl)filter` pattern, `CameraControls.cs:307`), null cast → property not offered.
- Device identity = moniker order, matching `WebcamScan` (:281-285) — unchanged from today.
- Scoping honesty: only what the current code already reaches (DirectShowLib monikers +
  `IAMVideoProcAmp`/`IAMCameraControl`); no capture-graph manipulation, no native dialog.

### 5.3 `CameraControlsAvFoundation.cs` (macOS)

Follows `MacCameraPermission.cs` for every interop decision (framework load at the absolute path,
`objc_getClass`/`sel_registerName`/`objc_msgSend`, CFString helpers). v1 scope — scalar selectors
only, no struct returns, no `CMTime` marshaling:

**Autorelease pool**: unlike `MacCameraPermission.cs` — which returns only an NSInteger and releases
its single CFString (:47-66) — this backend hands back ObjC objects through `objc_msgSend`: the
`devices` class property (a fresh autoreleased NSArray on every call), its `objectAtIndex:` elements
and the `localizedName` NSStrings. A plain worker thread has no autorelease pool, so every job body
that touches those objects runs wrapped in `objc_autoreleasePoolPush`/`objc_autoreleasePoolPop`
(plain C exports of libobjc — two one-line DllImports in this file); without it, each autoreleased
object leaks and libobjc logs "object autoreleased with no pool in place" per object, growing across
refreshes/applies. All interop helpers for this backend — including the `CFStringGetCString` read-out
helper for `localizedName` — live in THIS file; `MacCameraPermission.cs` is not extended (it is owned
by no work unit and stays read-only).

| Canonical | Selector(s) | Kind / units |
|---|---|---|
| Zoom | `videoZoomFactor` get/set; `minAvailableVideoZoomFactor` / `maxAvailableVideoZoomFactor` | Integer, Scale = 100 (so 100 = 1×, matching the UVC convention and the op's stored default) |
| Focus | `focusMode` / `setFocusMode:`; `isFocusModeSupported:` | Menu: 0 Locked / 1 Auto / 2 Continuous (auto = mode ≠ 0) |
| Exposure | `exposureMode` / `setExposureMode:` | Menu: 0 Locked / 1 Auto / 2 Continuous / 3 Custom (auto = 1 or 2) |
| WhiteBalance | `whiteBalanceMode` / `setWhiteBalanceMode:`; `isWhiteBalanceModeSupported:` | Menu: 0 Locked / 1 Auto / 2 Continuous (auto = 1 or 2) |

Pan/Tilt/Roll/Iris/Gain/… are **not** faked — the catalog omits them (§1.3). All writes are wrapped
in `lockForConfiguration:` (pass NULL for the out NSError) / `unlockForConfiguration`. Devices are
resolved by `localizedName` from the `devices` class property (NSArray traversal via plain
`count` / `objectAtIndex:` messages), matching `WebcamScan`'s list; without camera permission the
list is empty and the catalog is empty with the permission message the op already shows.

Extensions behind the v1 line, in order of value: custom exposure duration/ISO
(`setExposureModeCustomWithDuration:ISO:completionHandler:`, `CMTime` struct argument),
device white-balance gains (`setWhiteBalanceModeLockedWithDeviceWhiteBalanceGains:`, struct
argument, `[1.0, maxWhiteBalanceGain]`). Each needs struct-by-value/stret marshaling — deliberate
follow-up, not v1. If hardware verification shows mode setting requires a running capture session,
the backend still reports the honest catalog and the op surfaces the write failure — never a silent
no-op.

---

## 6. Work units (disjoint file ownership, in order)

1. **shared-control-model** (kind shared) — model + registry + worker + op integration. Rewrites
   `CameraControls.cs` in place (its only consumer is `VideoDeviceInput`), adds
   `CameraControlWorker.cs`. With no backend registered every platform degrades to the honest
   "device controls are not available" messaging — build and op stay green.
2. **linux-v4l2** (kind linux) — `CameraControlsV4L2.cs`. Acceptance: the catalog on `/dev/video0`
   matches the §1.1 table (19 walk entries → 17 controls + 2 skipped class markers, Power Line
   Frequency menu items present, INACTIVE on WB-Temp/Focus-Abs while autos are on) and `/dev/video2`
   reports the DJI's real Pan/Tilt/Zoom ranges.
3. **windows-directshow** (kind windows) — `CameraControlsDirectShow.cs`. Acceptance: catalog lists
   exactly the properties the device implements (incl. `ColorEnable` when present), auto flags read
   back, auto engage/leave works.
4. **macos-avfoundation** (kind macos) — `CameraControlsAvFoundation.cs`. Acceptance: honest catalog
   on real hardware (or empty + permission message); zoom set under `lockForConfiguration` verified.

File ownership:

| Unit | Files |
|---|---|
| shared-control-model | `Operators/Video/Utils/CameraControls.cs`, `Operators/Video/Utils/CameraControlWorker.cs` (new), `Operators/Video/Symbols/Lib/io/video/VideoDeviceInput.cs`, `Operators/Video/Symbols/Lib/io/video/VideoDeviceInput.t3`, `Operators/Video/Symbols/Lib/io/video/VideoDeviceInput.t3ui` |
| linux-v4l2 | `Operators/Video/Utils/CameraControlsV4L2.cs` (new) |
| windows-directshow | `Operators/Video/Utils/CameraControlsDirectShow.cs` (new) |
| macos-avfoundation | `Operators/Video/Utils/CameraControlsAvFoundation.cs` (new) |

`MacCameraPermission.cs` is read-only for every unit — referenced as the interop pattern, extended by
none; the macOS backend redeclares its own DllImports (including `CFStringGetCString`) inside
`CameraControlsAvFoundation.cs`, keeping the ownership table disjoint.

No `.csproj` changes — SDK-style globbing picks up the new files
(`Operators/Video/Video.csproj` uses `Microsoft.NET.Sdk`), no new packages are needed
(DirectShowLib 2.1.0 already referenced; Linux uses libc P/Invokes; macOS uses libobjc/CoreFoundation
as `MacCameraPermission.cs` already does). Transitional note: between unit 1 and the platform units
the op reports controls as unavailable on that platform — land all four before shipping the branch.

---

## 7. Risks

- **Transitional window**: after unit 1, before units 2-4, control I/O is unavailable platform-wide.
  Acceptable if the units land together; called out so nobody ships the intermediate state.
- **`[ModuleInitializer]` is a less common idiom in this codebase** — each backend file needs the
  registration call documented in place; the registry's null path must stay the tested default.
- **ioctl marshaling**: `ByValTStr` name fields under `Pack = 1` must be re-verified at runtime by
  unit 2 (the Python probe proves the wire format; the C# struct needs one live check that names
  decode — `WebcamScan.cs:180-212` is the precedent that these layouts marshal correctly).
- **Animated inputs**: coalescing in `EnqueueApply` is load-bearing — it is pinned to the
  `ConcurrentDictionary` replace-on-enqueue store (§3); without it an animated Pan queues a set per
  changed value.
- **Deferred applies** resolve only when the device's catalog arrives; if a device vanishes before
  any catalog could be built, the deferred applies are dropped at device change/Dispose with the
  unsupported warning — the configured values were never sent, and Status says so.
- **Stale INACTIVE/VOLATILE states** between refreshes — mitigated by the post-apply partner
  re-read (§3); a device changing its own state out-of-band (VOLATILE) is only reflected on next
  refresh, same as the DirectShow dialog without pressing F5.
- **Windows device-index identity** is moniker order (cached per session by `WebcamScan`) — a
  hot-plug reshuffles indices until Reconnect; pre-existing behavior, unchanged.
- **macOS**: controls may require a running capture session; v1 keeps scalar-only marshaling and
  honest failures. `Roll`'s absence on Linux and AVFoundation's missing PTZ controls are platform
  truths the catalog surfaces rather than hides.
